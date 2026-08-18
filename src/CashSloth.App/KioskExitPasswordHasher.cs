using System.Security.Cryptography;
using System.Text;

namespace CashSloth.App;

internal static class KioskExitPasswordHasher
{
    private const string Prefix = "sha256-v1";

    internal static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = ComputeHash(password, salt);
        return $"{Prefix}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    internal static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = ComputeHash(password, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var input = new byte[salt.Length + passwordBytes.Length];
        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(passwordBytes, 0, input, salt.Length, passwordBytes.Length);
        return SHA256.HashData(input);
    }
}
