using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashSloth.Contracts;
using Microsoft.IdentityModel.Tokens;

namespace CashSloth.App;

internal sealed class CashSlothServerException(int statusCode, string code, string message) : Exception(message)
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
}

internal sealed class CashSlothServerClient : IDisposable
{
    private readonly CashSlothServerStorage _storage;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal CashSlothServerClient(CashSlothServerStorage storage, HttpMessageHandler? handler = null)
    {
        _storage = storage;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        Session = RestoreValidOfflineSession();
    }

    internal CashSlothClientConnection? Connection => _storage.LoadConnection();
    internal CashSlothClientSession? Session { get; private set; }
    internal bool IsPaired => Connection?.DeviceId is not null;
    internal event Action<UserProfileResponse?>? SessionChanged;

    internal ServerTrustDocument ValidateTrustFile(string path)
    {
        var trust = JsonSerializer.Deserialize<ServerTrustDocument>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException("Trust-Datei ist ungültig.");
        if (trust.Version != 1 ||
            string.IsNullOrWhiteSpace(trust.ServerId) ||
            string.IsNullOrWhiteSpace(trust.KeyId) ||
            string.IsNullOrWhiteSpace(trust.PublicKey) ||
            string.IsNullOrWhiteSpace(trust.Fingerprint) ||
            !Uri.TryCreate(trust.HttpsUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Trust-Datei enthält keine gültige HTTPS-Serveradresse.");
        }
        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(trust.PublicKey);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
            if (read != publicKey.Length || ecdsa.KeySize != 256)
            {
                throw new CryptographicException();
            }
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("Trust-Datei enthält keinen gültigen ECDSA-P-256-Schlüssel.");
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(fingerprint),
                Encoding.ASCII.GetBytes(trust.Fingerprint.ToLowerInvariant())))
        {
            throw new InvalidDataException("Fingerprint der Trust-Datei stimmt nicht mit dem Serverschlüssel überein.");
        }
        return trust;
    }

    internal void AcceptTrust(ServerTrustDocument trust)
    {
        var current = Connection;
        var sameServer = current is not null &&
                         current.Trust.ServerId == trust.ServerId &&
                         current.Trust.KeyId == trust.KeyId;
        _storage.SaveConnection(new CashSlothClientConnection(
            trust,
            sameServer ? current!.DeviceId : null,
            sameServer ? current!.DeviceName : null));
        if (!sameServer)
        {
            Session = null;
            _storage.ClearSession();
        }
    }

    internal async Task VerifyServerInfoAsync(CancellationToken cancellationToken = default)
    {
        var connection = RequireConnection(requireDevice: false);
        var info = await GetPublicAsync<ServerInfoResponse>("api/v1/server/info", cancellationToken);
        if (info.ServerId != connection.Trust.ServerId || info.KeyId != connection.Trust.KeyId)
        {
            throw new CashSlothServerException(409, "server_trust_mismatch", "Der erreichbare Server passt nicht zur importierten Trust-Datei.");
        }
    }

    internal async Task<DevicePairResponse> PairAsync(
        string pairingCode,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var connection = RequireConnection(requireDevice: false);
        await VerifyServerInfoAsync(cancellationToken);
        var code = new string(pairingCode.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var name = deviceName.Trim();
        using var key = _storage.LoadOrCreateDeviceKey();
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var proofText = $"cashsloth-pair-v1\n{code}\n{name}\n{publicKey}";
        var signature = Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256));
        var response = await SendPublicAsync<DevicePairRequest, DevicePairResponse>(
            HttpMethod.Post,
            "api/v1/devices/pair",
            new DevicePairRequest(code, name, publicKey, signature),
            cancellationToken);
        _storage.SaveConnection(connection with { DeviceId = response.DeviceId, DeviceName = response.DeviceName });
        return response;
    }

    internal async Task<RegistrationResponse> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var proof = await CreateProofAsync("register", BuildPayloadHash(username, password), cancellationToken);
        return await SendPublicAsync<RegisterRequest, RegistrationResponse>(
            HttpMethod.Post,
            "api/v1/auth/register",
            new RegisterRequest(username, password, proof),
            cancellationToken);
    }

    internal async Task<CashSlothClientSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var proof = await CreateProofAsync("login", BuildPayloadHash(username, password), cancellationToken);
        var response = await SendPublicAsync<LoginRequest, AuthTokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/login",
            new LoginRequest(username, password, proof),
            cancellationToken);
        return SaveAndValidateSession(response);
    }

    internal async Task<CashSlothClientSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var session = Session ?? _storage.LoadSession()
            ?? throw new CashSlothServerException(401, "authentication_required", "Es ist keine Sitzung gespeichert.");
        if (session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            ClearSession();
            throw new CashSlothServerException(401, "refresh_token_expired", "Die gespeicherte Sitzung ist abgelaufen.");
        }
        try
        {
            var proof = await CreateProofAsync("refresh", BuildPayloadHash(session.RefreshToken), cancellationToken);
            var response = await SendPublicAsync<RefreshRequest, AuthTokenResponse>(
                HttpMethod.Post,
                "api/v1/auth/refresh",
                new RefreshRequest(session.RefreshToken, proof),
                cancellationToken);
            return SaveAndValidateSession(response);
        }
        catch (CashSlothServerException exception) when (exception.StatusCode is 401 or 403)
        {
            ClearSession();
            throw;
        }
    }

    internal async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var session = Session;
        try
        {
            if (session is not null && IsAccessTokenLocallyValid(session.AccessToken, out _))
            {
                await SendAuthorizedAsync<LogoutRequest, object?>(
                    HttpMethod.Post,
                    "api/v1/auth/logout",
                    new LogoutRequest(session.RefreshToken),
                    cancellationToken,
                    allowNoContent: true);
            }
        }
        finally
        {
            ClearSession();
        }
    }

    internal Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            "api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword),
            cancellationToken);

    internal async Task<UserProfileResponse> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = await SendAuthorizedAsync<object?, UserProfileResponse>(
            HttpMethod.Get,
            "api/v1/auth/me",
            null,
            cancellationToken)
            ?? throw new InvalidDataException("Server lieferte kein Benutzerprofil.");

        if (Session is not null)
        {
            Session = Session with { User = profile };
            _storage.SaveSession(Session);
            SessionChanged?.Invoke(profile);
        }
        return profile;
    }

    internal async Task<IReadOnlyList<PresetSummaryResponse>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<PresetSummaryResponse>>(HttpMethod.Get, "api/v1/presets", null, cancellationToken)
        ?? [];

    internal async Task<PresetDocument> GetPresetAsync(string id, CancellationToken cancellationToken = default) =>
        await GetAndCachePresetAsync(id, cancellationToken);

    internal async Task<PresetDocument> GetActivePresetWithOfflineFallbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var preset = await SendAuthorizedAsync<object?, PresetDocument>(HttpMethod.Get, "api/v1/presets/active", null, cancellationToken)
                ?? throw new InvalidDataException("Server lieferte kein aktives Preset.");
            _storage.SavePresetCache(preset);
            return preset;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (Session is null || !IsAccessTokenLocallyValid(Session.AccessToken, out _))
            {
                throw new CashSlothServerException(503, "offline_session_expired", "Server ist offline und das lokale 12-Stunden-Token ist nicht mehr gültig.");
            }
            return _storage.LoadPresetCache()
                ?? throw new CashSlothServerException(503, "offline_preset_missing", "Server ist offline und es existiert noch kein Preset-Cache.");
        }
    }

    internal async Task<PresetWriteResponse> CreatePresetAsync(PresetDocument preset, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<PresetDocument, PresetWriteResponse>(HttpMethod.Post, "api/v1/presets", preset, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Presetbestätigung.");

    internal async Task<PresetWriteResponse> UpdatePresetAsync(PresetDocument preset, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<PresetDocument, PresetWriteResponse>(HttpMethod.Put, $"api/v1/presets/{Uri.EscapeDataString(preset.Id)}", preset, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Presetbestätigung.");

    internal Task SetActivePresetAsync(string id, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/presets/{Uri.EscapeDataString(id)}/active", new { }, cancellationToken);

    internal async Task<IReadOnlyList<AdminAccountResponse>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<AdminAccountResponse>>(HttpMethod.Get, "api/v1/admin/accounts", null, cancellationToken)
        ?? [];

    internal Task SetAccountApprovalAsync(string id, bool approved, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/approval", new AdminAccountApprovalRequest(approved), cancellationToken);

    internal Task SetAccountRoleAsync(string id, string role, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/role", new AdminAccountRoleRequest(role), cancellationToken);

    internal Task SetAccountActiveAsync(string id, bool active, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/status", new AdminAccountStatusRequest(active), cancellationToken);

    internal async Task<string> ResetAccountPasswordAsync(string id, CancellationToken cancellationToken = default) =>
        (await SendAuthorizedAsync<object?, AdminPasswordResetResponse>(HttpMethod.Post, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/password-reset", null, cancellationToken))?.TemporaryPassword
        ?? throw new InvalidDataException("Server lieferte kein temporäres Passwort.");

    internal async Task<ExchangeRateResponse> GetExchangeRatesAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, ExchangeRateResponse>(HttpMethod.Get, "api/v1/reference/exchange-rates", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Wechselkurse.");

    internal async Task<TranslationResolveResponse> ResolveTranslationsAsync(
        TranslationResolveRequest request,
        CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<TranslationResolveRequest, TranslationResolveResponse>(
            HttpMethod.Post,
            "api/v1/reference/translations/resolve",
            request,
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Übersetzungsantwort.");

    internal bool IsAccessTokenLocallyValid(string token, out ClaimsPrincipal? principal)
    {
        principal = null;
        var connection = Connection;
        if (connection is null)
        {
            return false;
        }
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(connection.Trust.PublicKey), out _);
            var key = new ECDsaSecurityKey(ecdsa) { KeyId = connection.Trust.KeyId };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = $"cashsloth-server:{connection.Trust.ServerId}",
                ValidateAudience = true,
                ValidAudience = "cashsloth-clients",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            }, out _);
            return connection.DeviceId is { } deviceId &&
                   Guid.TryParse(principal.FindFirst("device_id")?.Value, out var tokenDeviceId) &&
                   tokenDeviceId == deviceId;
        }
        catch (Exception exception) when (exception is SecurityTokenException or CryptographicException or FormatException)
        {
            principal = null;
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private CashSlothClientSession? RestoreValidOfflineSession()
    {
        var session = _storage.LoadSession();
        if (session is not null && session.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return session;
        }
        _storage.ClearSession();
        return null;
    }

    private CashSlothClientSession SaveAndValidateSession(AuthTokenResponse response)
    {
        if (!IsAccessTokenLocallyValid(response.AccessToken, out var principal))
        {
            throw new CashSlothServerException(401, "invalid_server_token", "Server lieferte ein Token, das nicht zum gepinnten Serverschlüssel passt.");
        }
        if (principal?.FindFirst("sub")?.Value != response.User.Id)
        {
            throw new CashSlothServerException(401, "invalid_server_token", "Token und Benutzerantwort passen nicht zusammen.");
        }
        var session = new CashSlothClientSession(
            response.AccessToken,
            response.AccessTokenExpiresAtUtc,
            response.RefreshToken,
            response.RefreshTokenExpiresAtUtc,
            response.User);
        _storage.SaveSession(session);
        Session = session;
        SessionChanged?.Invoke(session.User);
        return session;
    }

    private async Task<PresetDocument> GetAndCachePresetAsync(string id, CancellationToken cancellationToken)
    {
        var preset = await SendAuthorizedAsync<object?, PresetDocument>(
            HttpMethod.Get,
            $"api/v1/presets/{Uri.EscapeDataString(id)}",
            null,
            cancellationToken)
            ?? throw new InvalidDataException("Server lieferte kein Preset.");
        if (preset.IsActive)
        {
            _storage.SavePresetCache(preset);
        }
        return preset;
    }

    private void ClearSession()
    {
        var hadSession = Session is not null || _storage.LoadSession() is not null;
        Session = null;
        _storage.ClearSession();
        if (hadSession)
        {
            SessionChanged?.Invoke(null);
        }
    }

    private async Task<DeviceProof> CreateProofAsync(
        string purpose,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var connection = RequireConnection(requireDevice: true);
        var challenge = await SendPublicAsync<DeviceChallengeRequest, DeviceChallengeResponse>(
            HttpMethod.Post,
            "api/v1/devices/challenge",
            new DeviceChallengeRequest(connection.DeviceId!.Value, purpose),
            cancellationToken);
        var proofText = $"cashsloth-device-proof-v1\n{purpose}\n{challenge.ChallengeId:N}\n{challenge.Nonce}\n{payloadHash}";
        using var key = _storage.LoadOrCreateDeviceKey();
        var signature = Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256));
        return new DeviceProof(connection.DeviceId.Value, challenge.ChallengeId, signature);
    }

    private CashSlothClientConnection RequireConnection(bool requireDevice)
    {
        var connection = Connection
            ?? throw new CashSlothServerException(400, "server_trust_required", "Importiere zuerst die CashSloth-Trust-Datei.");
        if (requireDevice && connection.DeviceId is null)
        {
            throw new CashSlothServerException(400, "device_pairing_required", "Kopple dieses Gerät zuerst mit dem Server.");
        }
        return connection;
    }

    private async Task<T> GetPublicAsync<T>(string path, CancellationToken cancellationToken) =>
        await SendPublicAsync<object?, T>(HttpMethod.Get, path, null, cancellationToken);

    private async Task<TResponse> SendPublicAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateMessage(method, path, request, includeAuthentication: false);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse?> SendAuthorizedAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        if (Session is null || !IsAccessTokenLocallyValid(Session.AccessToken, out _))
        {
            await RefreshAsync(cancellationToken);
        }

        using (var message = CreateMessage(method, path, request, includeAuthentication: true))
        using (var response = await _httpClient.SendAsync(message, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                if (allowNoContent && response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default;
                }
                return await ReadResponseAsync<TResponse>(response, cancellationToken);
            }
        }

        // A locally valid token can still be rejected after a role/session change.
        // Refresh once to synchronize with the server's authoritative account state.
        await RefreshAsync(cancellationToken);
        using var retryMessage = CreateMessage(method, path, request, includeAuthentication: true);
        using var retryResponse = await _httpClient.SendAsync(retryMessage, cancellationToken);
        if (allowNoContent && retryResponse.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }
        return await ReadResponseAsync<TResponse>(retryResponse, cancellationToken);
    }

    private async Task SendNoContentAsync<TRequest>(HttpMethod method, string path, TRequest request, CancellationToken cancellationToken) =>
        await SendAuthorizedAsync<TRequest, object?>(method, path, request, cancellationToken, allowNoContent: true);

    private HttpRequestMessage CreateMessage<TRequest>(HttpMethod method, string path, TRequest request, bool includeAuthentication)
    {
        var connection = RequireConnection(requireDevice: false);
        var root = connection.Trust.HttpsUrl.TrimEnd('/') + "/";
        var message = new HttpRequestMessage(method, new Uri(new Uri(root), path.TrimStart('/')));
        if (request is not null)
        {
            message.Content = JsonContent.Create(request, options: _jsonOptions);
        }
        if (includeAuthentication)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session?.AccessToken);
        }
        return message;
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default!;
            }
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Serverantwort ist leer.");
        }

        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(_jsonOptions, cancellationToken); } catch { }
        throw new CashSlothServerException(
            (int)response.StatusCode,
            error?.Code ?? "server_request_failed",
            error?.Message ?? $"Server antwortete mit HTTP {(int)response.StatusCode}.");
    }

    private static string BuildPayloadHash(params string[] values) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values))));

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
