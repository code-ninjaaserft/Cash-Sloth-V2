using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashSloth.Contracts;

namespace CashSloth.App;

internal sealed record CashSlothClientConnection(ServerTrustDocument Trust, Guid? DeviceId, string? DeviceName);

internal sealed record CashSlothClientSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserProfileResponse User);

internal sealed class CashSlothServerStorage
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("CashSloth.CSV2.ServerClient.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _root;

    internal CashSlothServerStorage()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CashSloth",
            "Client"))
    {
    }

    internal CashSlothServerStorage(string root)
    {
        _root = Path.GetFullPath(root);
    }

    private string ConnectionPath => Path.Combine(_root, "server-connection.json");
    private string DeviceKeyPath => Path.Combine(_root, "device-private-key.bin");
    private string SessionPath => Path.Combine(_root, "server-session.bin");
    internal string PresetCachePath => Path.Combine(_root, "active-server-preset.json");

    internal CashSlothClientConnection? LoadConnection() => ReadJson<CashSlothClientConnection>(ConnectionPath);

    internal void SaveConnection(CashSlothClientConnection connection) => WriteJson(ConnectionPath, connection);

    internal ECDsa LoadOrCreateDeviceKey()
    {
        Directory.CreateDirectory(_root);
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!File.Exists(DeviceKeyPath))
        {
            var privateKey = key.ExportPkcs8PrivateKey();
            try
            {
                File.WriteAllBytes(DeviceKeyPath, Protect(privateKey));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
            return key;
        }

        var protectedKey = File.ReadAllBytes(DeviceKeyPath);
        var plainKey = Unprotect(protectedKey);
        try
        {
            key.ImportPkcs8PrivateKey(plainKey, out _);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainKey);
        }
    }

    internal CashSlothClientSession? LoadSession()
    {
        if (!File.Exists(SessionPath))
        {
            return null;
        }
        try
        {
            var plain = Unprotect(File.ReadAllBytes(SessionPath));
            try
            {
                return JsonSerializer.Deserialize<CashSlothClientSession>(plain, JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    internal void SaveSession(CashSlothClientSession session)
    {
        Directory.CreateDirectory(_root);
        var plain = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        try
        {
            File.WriteAllBytes(SessionPath, Protect(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    internal void ClearSession()
    {
        if (File.Exists(SessionPath))
        {
            File.Delete(SessionPath);
        }
    }

    internal PresetDocument? LoadPresetCache() => ReadJson<PresetDocument>(PresetCachePath);

    internal void SavePresetCache(PresetDocument preset) => WriteJson(PresetCachePath, preset);

    private T? ReadJson<T>(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                : default;
        }
        catch
        {
            return default;
        }
    }

    private void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(_root);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(byte[] protectedBytes) =>
        ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}
