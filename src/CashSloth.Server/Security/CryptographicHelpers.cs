using System.Security.Cryptography;
using System.Text;

namespace CashSloth.Server.Security;

public static class CryptographicHelpers
{
    public static string Sha256Base64Url(string value) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Fingerprint(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(padded);
    }

    public static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static string RandomToken(int byteCount = 32) => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));
}
