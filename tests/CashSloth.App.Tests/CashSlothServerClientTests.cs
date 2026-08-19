using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CashSloth.App;
using CashSloth.Contracts;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class CashSlothServerClientTests
{
    [Fact]
    public async Task ValidatesTrustDocumentFingerprint()
    {
        var root = CreateTempDirectory();
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = key.ExportSubjectPublicKeyInfo();
            var trust = CreateTrust(
                publicKey: Convert.ToBase64String(publicKey),
                fingerprint: Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant());
            var path = Path.Combine(root, "server.cashsloth-trust");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(trust));

            using var client = new CashSlothServerClient(new CashSlothServerStorage(root));
            Assert.Equal(trust, client.ValidateTrustFile(path));

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(trust with { Fingerprint = new string('0', 64) }));
            Assert.Throws<InvalidDataException>(() => client.ValidateTrustFile(path));

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(trust with { PublicKey = string.Empty }));
            Assert.Throws<InvalidDataException>(() => client.ValidateTrustFile(path));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void AcceptingTrustPreservesPairingOnlyForTheSameServerKey()
    {
        var root = CreateTempDirectory();
        try
        {
            var storage = new CashSlothServerStorage(root);
            var originalTrust = CreateTrust();
            var deviceId = Guid.NewGuid();
            storage.SaveConnection(new CashSlothClientConnection(originalTrust, deviceId, "Kasse 1"));

            using var client = new CashSlothServerClient(storage);
            client.AcceptTrust(originalTrust with { HttpsUrl = "https://new.example.test" });
            Assert.Equal(deviceId, client.Connection?.DeviceId);

            client.AcceptTrust(originalTrust with { Fingerprint = "different-fingerprint" });
            Assert.Null(client.Connection?.DeviceId);
            Assert.Null(client.Connection?.DeviceName);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConcurrentUnauthorizedRequestsShareOneRotatingTokenRefresh()
    {
        var root = CreateTempDirectory();
        try
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = signingKey.ExportSubjectPublicKeyInfo();
            var trust = CreateTrust(
                Convert.ToBase64String(publicKey),
                Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant());
            var deviceId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var profile = new UserProfileResponse(
                Guid.NewGuid().ToString("N"),
                "admin",
                CashSlothRoles.Admin,
                true,
                true,
                false,
                deviceId,
                sessionId);
            var tokenKey = new ECDsaSecurityKey(signingKey) { KeyId = trust.KeyId };
            var initialToken = CreateAccessToken(tokenKey, trust, profile);
            var refreshedToken = CreateAccessToken(tokenKey, trust, profile);
            var storage = new CashSlothServerStorage(root);
            storage.SaveConnection(new CashSlothClientConnection(trust, deviceId, "Kasse 1"));
            storage.SaveSession(new CashSlothClientSession(
                initialToken,
                DateTimeOffset.UtcNow.AddHours(12),
                "refresh-1",
                DateTimeOffset.UtcNow.AddDays(30),
                profile));
            using var handler = new RefreshRaceHandler(initialToken, refreshedToken, profile);
            using var client = new CashSlothServerClient(storage, handler);
            Assert.NotEqual(initialToken, refreshedToken);
            Assert.True(client.IsAccessTokenLocallyValid(initialToken, out _));
            Assert.True(client.IsAccessTokenLocallyValid(refreshedToken, out _));

            var first = client.GetExchangeRatesAsync();
            var second = client.GetExchangeRatesAsync();
            await Task.WhenAll(first, second);

            Assert.Equal(1, handler.RefreshCalls);
            Assert.Equal("refresh-2", client.Session?.RefreshToken);
            Assert.NotNull(storage.LoadSession());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static string CreateAccessToken(
        ECDsaSecurityKey signingKey,
        ServerTrustDocument trust,
        UserProfileResponse profile)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: $"cashsloth-server:{trust.ServerId}",
            audience: "cashsloth-clients",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, profile.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.Name, profile.Username),
                new Claim(ClaimTypes.Role, profile.Role),
                new Claim("device_id", profile.DeviceId.ToString("N")),
                new Claim("session_id", profile.SessionId.ToString("N"))
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddHours(12),
            signingCredentials: new SigningCredentials(
                signingKey,
                SecurityAlgorithms.EcdsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class RefreshRaceHandler(
        string initialToken,
        string refreshedToken,
        UserProfileResponse profile) : HttpMessageHandler
    {
        private int _refreshCalls;

        internal int RefreshCalls => _refreshCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/v1/devices/challenge", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new DeviceChallengeResponse(
                    Guid.NewGuid(),
                    Guid.NewGuid().ToString("N"),
                    "refresh",
                    DateTimeOffset.UtcNow.AddMinutes(2)));
            }

            if (path.EndsWith("/api/v1/auth/refresh", StringComparison.Ordinal))
            {
                var call = Interlocked.Increment(ref _refreshCalls);
                if (call > 1)
                {
                    return Json(HttpStatusCode.Unauthorized, new ApiError(
                        "invalid_refresh_token",
                        "Refresh token was already rotated.",
                        null,
                        Guid.NewGuid().ToString("N")));
                }
                await Task.Delay(100, cancellationToken);
                return Json(HttpStatusCode.OK, new AuthTokenResponse(
                    refreshedToken,
                    DateTimeOffset.UtcNow.AddHours(12),
                    "refresh-2",
                    DateTimeOffset.UtcNow.AddDays(30),
                    profile));
            }

            if (path.EndsWith("/api/v1/reference/exchange-rates", StringComparison.Ordinal))
            {
                var bearer = request.Headers.Authorization?.Parameter;
                if (string.Equals(bearer, initialToken, StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.Unauthorized, new ApiError(
                        "authentication_required",
                        "Refresh authentication.",
                        null,
                        Guid.NewGuid().ToString("N")));
                }
                if (string.Equals(bearer, refreshedToken, StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.OK, new ExchangeRateResponse(
                        "CHF",
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        DateTimeOffset.UtcNow,
                        false,
                        new Dictionary<string, decimal> { ["CHF"] = 1m }));
                }
            }

            return Json(HttpStatusCode.NotFound, new ApiError(
                "not_found",
                "Unexpected test request.",
                null,
                Guid.NewGuid().ToString("N")));
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T value) => new(status)
        {
            Content = JsonContent.Create(value)
        };
    }

    private static ServerTrustDocument CreateTrust(
        string publicKey = "public-key",
        string fingerprint = "fingerprint") =>
        new(1, "server-1", "https://api.example.test", publicKey, "key-1", fingerprint);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CashSloth.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CashSloth.App.Tests"));
        if (fullPath.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
