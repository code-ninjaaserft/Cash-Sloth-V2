using System.Security.Cryptography;
using System.Text.Json;
using CashSloth.App;
using CashSloth.Contracts;
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

            client.AcceptTrust(originalTrust with { ServerId = "different-server" });
            Assert.Null(client.Connection?.DeviceId);
            Assert.Null(client.Connection?.DeviceName);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
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
