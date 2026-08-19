using System.Security.Cryptography;
using System.Text;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed class DevicePairingService(ServerDbContext db, AuditService audit)
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public async Task<(string Code, DateTimeOffset ExpiresAtUtc)> CreatePairingCodeAsync(
        string actor = "local-console",
        CancellationToken cancellationToken = default)
    {
        var code = CreateCode(10);
        var now = DateTimeOffset.UtcNow;
        db.PairingCodes.Add(new PairingCode
        {
            Id = Guid.NewGuid(),
            CodeHash = CryptographicHelpers.Sha256Base64Url(code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10)
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "pairing-code.create", "pairing-code", detail: "Gültig für 10 Minuten.", cancellationToken: cancellationToken);
        return (code, now.AddMinutes(10));
    }

    public async Task<DevicePairResponse> PairAsync(DevicePairRequest request, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.PairingCode);
        if (code.Length != 10)
        {
            throw new ApiProblemException(400, "invalid_pairing_code", "Pairingcode ist ungültig.");
        }

        var codeHash = CryptographicHelpers.Sha256Base64Url(code);
        var pairing = await db.PairingCodes.SingleOrDefaultAsync(value => value.CodeHash == codeHash, cancellationToken);
        if (pairing is null)
        {
            throw new ApiProblemException(401, "invalid_pairing_code", "Pairingcode ist ungültig.");
        }

        var now = DateTimeOffset.UtcNow;
        if (pairing.UsedAtUtc is not null || pairing.ExpiresAtUtc <= now)
        {
            throw new ApiProblemException(401, "expired_pairing_code", "Pairingcode ist abgelaufen oder wurde bereits verwendet.");
        }

        var deviceName = request.DeviceName.Trim();
        if (deviceName.Length is < 1 or > 100)
        {
            throw new ApiProblemException(400, "invalid_device_name", "Gerätename muss 1 bis 100 Zeichen lang sein.");
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(request.PublicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || key.KeySize != 256)
            {
                throw new CryptographicException();
            }
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new ApiProblemException(400, "invalid_device_key", "Geräteschlüssel muss ein gültiger ECDSA-P-256-Schlüssel sein.");
        }

        var proofText = $"cashsloth-pair-v1\n{code}\n{deviceName}\n{request.PublicKey}";
        if (!DeviceProofService.VerifySignature(request.PublicKey, proofText, request.Signature))
        {
            pairing.FailedAttempts++;
            await db.SaveChangesAsync(cancellationToken);
            throw new ApiProblemException(401, "invalid_device_signature", "Gerätesignatur ist ungültig.");
        }

        var fingerprint = CryptographicHelpers.Fingerprint(publicKey);
        if (await db.Devices.AnyAsync(value => value.PublicKeyFingerprint == fingerprint, cancellationToken))
        {
            throw new ApiProblemException(409, "device_already_paired", "Dieser Geräteschlüssel ist bereits gekoppelt.");
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = deviceName,
            PublicKey = request.PublicKey,
            PublicKeyFingerprint = fingerprint,
            IsActive = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };
        pairing.UsedAtUtc = now;
        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync($"device:{device.Id:N}", "device.pair", "device", device.Id.ToString("N"), device.Name, cancellationToken: cancellationToken);
        return new DevicePairResponse(device.Id, device.Name, now);
    }

    private static string NormalizeCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string CreateCode(int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(Alphabet[value % Alphabet.Length]);
        }
        return builder.ToString();
    }
}
