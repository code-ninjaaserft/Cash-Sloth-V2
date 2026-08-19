using System.Security.Cryptography;
using System.Text;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Security;

public sealed class DeviceProofService(ServerDbContext db)
{
    private static readonly string[] AllowedPurposes = ["register", "login", "refresh"];

    public async Task<DeviceChallengeResponse> CreateChallengeAsync(
        Guid deviceId,
        string purpose,
        CancellationToken cancellationToken)
    {
        var normalizedPurpose = purpose.Trim().ToLowerInvariant();
        if (!AllowedPurposes.Contains(normalizedPurpose, StringComparer.Ordinal))
        {
            throw new ApiProblemException(400, "invalid_challenge_purpose", "Unbekannter Challenge-Zweck.");
        }

        var device = await db.Devices.SingleOrDefaultAsync(value => value.Id == deviceId, cancellationToken);
        if (device is null || !device.IsActive)
        {
            throw new ApiProblemException(404, "device_not_found", "Gerät wurde nicht gefunden oder ist gesperrt.");
        }

        var now = DateTimeOffset.UtcNow;
        var challenge = new DeviceChallenge
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Nonce = CryptographicHelpers.RandomToken(),
            Purpose = normalizedPurpose,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(2)
        };
        db.DeviceChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        return new DeviceChallengeResponse(challenge.Id, challenge.Nonce, challenge.Purpose, challenge.ExpiresAtUtc);
    }

    public async Task<Device> VerifyAndConsumeAsync(
        DeviceProof proof,
        string purpose,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var challenge = await db.DeviceChallenges
            .Include(value => value.Device)
            .SingleOrDefaultAsync(value => value.Id == proof.ChallengeId && value.DeviceId == proof.DeviceId, cancellationToken);

        if (challenge is null || challenge.UsedAtUtc is not null)
        {
            throw new ApiProblemException(401, "invalid_device_proof", "Geräte-Challenge ist ungültig oder wurde bereits verwendet.");
        }

        var now = DateTimeOffset.UtcNow;
        if (challenge.ExpiresAtUtc <= now || !string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal))
        {
            throw new ApiProblemException(401, "expired_device_challenge", "Geräte-Challenge ist abgelaufen oder hat den falschen Zweck.");
        }

        if (!challenge.Device.IsActive)
        {
            throw new ApiProblemException(403, "device_blocked", "Dieses Gerät ist gesperrt.");
        }

        var signedText = BuildProofText(
            purpose,
            challenge.Id,
            challenge.Nonce,
            payloadHash);
        if (!VerifySignature(challenge.Device.PublicKey, signedText, proof.Signature))
        {
            throw new ApiProblemException(401, "invalid_device_signature", "Gerätesignatur ist ungültig.");
        }

        challenge.UsedAtUtc = now;
        challenge.Device.LastSeenAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return challenge.Device;
    }

    public static string BuildProofText(string purpose, Guid challengeId, string nonce, string payloadHash) =>
        $"cashsloth-device-proof-v1\n{purpose}\n{challengeId:N}\n{nonce}\n{payloadHash}";

    public static string BuildPayloadHash(params string[] values) =>
        CryptographicHelpers.Sha256Base64Url(string.Join('\n', values));

    public static bool VerifySignature(string publicKeyBase64, string signedText, string signatureBase64Url)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(signedText),
                CryptographicHelpers.Base64UrlDecode(signatureBase64Url),
                HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
