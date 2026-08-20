using System.Security.Cryptography;
using System.Text;

namespace CashSloth.Server.Security;

public static class SecretProtector
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("CashSloth.Server.DPAPI.v1"));

    public static byte[] Protect(ReadOnlySpan<byte> plainBytes) =>
        ProtectedData.Protect(plainBytes.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) =>
        ProtectedData.Unprotect(protectedBytes.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public static void WriteProtectedText(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedBytes = Protect(Encoding.UTF8.GetBytes(value));
        File.WriteAllBytes(path, protectedBytes);
    }

    public static string? ReadProtectedText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var plainBytes = Unprotect(File.ReadAllBytes(path));
        try
        {
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}
