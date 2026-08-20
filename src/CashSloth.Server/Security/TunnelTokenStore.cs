using CashSloth.Server.Infrastructure;

namespace CashSloth.Server.Security;

public sealed class TunnelTokenStore(ServerPaths paths)
{
    public bool HasToken => File.Exists(paths.TunnelTokenPath);

    public void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Tunnel token must not be empty.", nameof(token));
        }

        SecretProtector.WriteProtectedText(paths.TunnelTokenPath, token.Trim());
    }

    public string Read() =>
        SecretProtector.ReadProtectedText(paths.TunnelTokenPath)
        ?? throw new InvalidOperationException("No Cloudflare tunnel token is configured.");
}
