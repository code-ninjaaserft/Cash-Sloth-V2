using System.Security.Cryptography;
using System.Text.Json;
using CashSloth.Contracts;
using CashSloth.Server.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace CashSloth.Server.Security;

public sealed class ServerKeyService : IDisposable
{
    private readonly ServerPaths _paths;
    private readonly ECDsa _ecdsa;
    private readonly SigningKeyMetadata _metadata;

    public ServerKeyService(ServerPaths paths)
    {
        _paths = paths;
        _paths.EnsureDirectories();
        (_ecdsa, _metadata) = LoadOrCreate();
        SecurityKey = new ECDsaSecurityKey(_ecdsa) { KeyId = _metadata.KeyId };
    }

    public ECDsaSecurityKey SecurityKey { get; }
    public string ServerId => _metadata.ServerId;
    public string KeyId => _metadata.KeyId;
    public string PublicKeyBase64 => Convert.ToBase64String(_ecdsa.ExportSubjectPublicKeyInfo());
    public string Fingerprint => CryptographicHelpers.Fingerprint(_ecdsa.ExportSubjectPublicKeyInfo());

    public ServerTrustDocument CreateTrustDocument(string publicUrl) => new(
        1,
        ServerId,
        publicUrl.TrimEnd('/'),
        PublicKeyBase64,
        KeyId,
        Fingerprint);

    public void ExportTrustFile(string path, string publicUrl)
    {
        var json = JsonSerializer.Serialize(CreateTrustDocument(publicUrl), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    public void Dispose() => _ecdsa.Dispose();

    private (ECDsa Key, SigningKeyMetadata Metadata) LoadOrCreate()
    {
        if (File.Exists(_paths.SigningKeyPath) && File.Exists(_paths.SigningKeyMetadataPath))
        {
            var key = ECDsa.Create();
            var plainKey = SecretProtector.Unprotect(File.ReadAllBytes(_paths.SigningKeyPath));
            try
            {
                key.ImportPkcs8PrivateKey(plainKey, out _);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainKey);
            }

            var metadata = JsonSerializer.Deserialize<SigningKeyMetadata>(
                File.ReadAllText(_paths.SigningKeyMetadataPath))
                ?? throw new InvalidDataException("Server signing-key metadata is invalid.");
            return (key, metadata);
        }

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        try
        {
            File.WriteAllBytes(_paths.SigningKeyPath, SecretProtector.Protect(privateKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }

        var keyId = CryptographicHelpers.Fingerprint(ecdsa.ExportSubjectPublicKeyInfo())[..16];
        var created = new SigningKeyMetadata(Guid.NewGuid().ToString("N"), keyId, DateTimeOffset.UtcNow);
        File.WriteAllText(
            _paths.SigningKeyMetadataPath,
            JsonSerializer.Serialize(created, new JsonSerializerOptions { WriteIndented = true }));
        return (ecdsa, created);
    }

    private sealed record SigningKeyMetadata(string ServerId, string KeyId, DateTimeOffset CreatedAtUtc);
}
