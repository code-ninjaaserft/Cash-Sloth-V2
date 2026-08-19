namespace CashSloth.Server.Infrastructure;

public sealed class ServerPaths
{
    public ServerPaths(string? dataPath = null)
    {
        ConfigRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CashSloth",
            "Server");
        DataRoot = string.IsNullOrWhiteSpace(dataPath) ? ConfigRoot : Path.GetFullPath(dataPath);
    }

    public string ConfigRoot { get; }
    public string DataRoot { get; }
    public string SettingsPath => Path.Combine(ConfigRoot, "server.settings.json");
    public string DatabasePath => Path.Combine(DataRoot, "cashsloth.server.sqlite3");
    public string BackupsPath => Path.Combine(DataRoot, "backups");
    public string DataProtectionKeysPath => Path.Combine(DataRoot, "data-protection-keys");
    public string SigningKeyPath => Path.Combine(DataRoot, "server-signing-key.bin");
    public string SigningKeyMetadataPath => Path.Combine(DataRoot, "server-signing-key.json");
    public string TunnelTokenPath => Path.Combine(DataRoot, "tunnel-token.bin");
    public string LogPath => Path.Combine(DataRoot, "logs");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(DataProtectionKeysPath);
        Directory.CreateDirectory(LogPath);
    }
}
