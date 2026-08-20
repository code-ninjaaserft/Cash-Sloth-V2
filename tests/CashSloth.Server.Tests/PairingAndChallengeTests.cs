using System.Security.Cryptography;
using System.Text;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Tests;

public sealed class PairingAndChallengeTests
{
    [Fact]
    public async Task PairingCode_IsSingleUse()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var pairing = scope.ServiceProvider.GetRequiredService<DevicePairingService>();
        var created = await pairing.CreatePairingCodeAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = CreatePairRequest(created.Code, "Kasse 1", key);

        var paired = await pairing.PairAsync(request, CancellationToken.None);
        Assert.Equal("Kasse 1", paired.DeviceName);

        var exception = await Assert.ThrowsAsync<ApiProblemException>(() =>
            pairing.PairAsync(request, CancellationToken.None));
        Assert.Equal("expired_pairing_code", exception.Code);
    }

    [Fact]
    public async Task PairingCode_RejectsExpiredAndGuessedValues()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var pairing = scope.ServiceProvider.GetRequiredService<DevicePairingService>();
        var created = await pairing.CreatePairingCodeAsync();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var row = await db.PairingCodes.SingleAsync();
        row.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var expired = await Assert.ThrowsAsync<ApiProblemException>(() =>
            pairing.PairAsync(CreatePairRequest(created.Code, "Kasse", key), CancellationToken.None));
        Assert.Equal("expired_pairing_code", expired.Code);

        var guessed = await Assert.ThrowsAsync<ApiProblemException>(() =>
            pairing.PairAsync(CreatePairRequest("AAAAAAAAAA", "Kasse", key), CancellationToken.None));
        Assert.Equal("invalid_pairing_code", guessed.Code);
    }

    [Fact]
    public async Task PairingAndChallenge_RejectWrongKeyAndChallengeReuse()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var pairing = scope.ServiceProvider.GetRequiredService<DevicePairingService>();
        var created = await pairing.CreatePairingCodeAsync();
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invalidRequest = CreatePairRequest(created.Code, "Kasse", deviceKey, signingKey: wrongKey);
        var invalid = await Assert.ThrowsAsync<ApiProblemException>(() =>
            pairing.PairAsync(invalidRequest, CancellationToken.None));
        Assert.Equal("invalid_device_signature", invalid.Code);

        var paired = await pairing.PairAsync(CreatePairRequest(created.Code, "Kasse", deviceKey), CancellationToken.None);
        var proofs = scope.ServiceProvider.GetRequiredService<DeviceProofService>();
        var challenge = await proofs.CreateChallengeAsync(paired.DeviceId, "login", CancellationToken.None);
        var payloadHash = DeviceProofService.BuildPayloadHash("alice", "secret");
        var proofText = DeviceProofService.BuildProofText("login", challenge.ChallengeId, challenge.Nonce, payloadHash);
        var signature = Base64Url(deviceKey.SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256));
        var proof = new DeviceProof(paired.DeviceId, challenge.ChallengeId, signature);

        await proofs.VerifyAndConsumeAsync(proof, "login", payloadHash, CancellationToken.None);
        var reused = await Assert.ThrowsAsync<ApiProblemException>(() =>
            proofs.VerifyAndConsumeAsync(proof, "login", payloadHash, CancellationToken.None));
        Assert.Equal("invalid_device_proof", reused.Code);
    }

    private static DevicePairRequest CreatePairRequest(
        string code,
        string deviceName,
        ECDsa publicKey,
        ECDsa? signingKey = null)
    {
        var normalizedCode = new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var publicKeyText = Convert.ToBase64String(publicKey.ExportSubjectPublicKeyInfo());
        var proofText = $"cashsloth-pair-v1\n{normalizedCode}\n{deviceName}\n{publicKeyText}";
        var signature = (signingKey ?? publicKey).SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256);
        return new DevicePairRequest(normalizedCode, deviceName, publicKeyText, Base64Url(signature));
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
