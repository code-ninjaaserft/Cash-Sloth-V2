using System.Text.Json;

namespace CashSloth.Server.Infrastructure;

public sealed record ServerSettings
{
    public string PublicUrl { get; init; } = string.Empty;
    public string DataPath { get; init; } = string.Empty;
    public string CloudflaredPath { get; init; } = string.Empty;
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public string UpdateManifestUrl { get; init; } = string.Empty;
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    public static ServerSettings CreateDefault()
    {
        var paths = new ServerPaths();
        return new ServerSettings
        {
            DataPath = paths.DataRoot,
            CloudflaredPath = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe")
        };
    }
}

public sealed class ServerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public ServerSettingsStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? new ServerPaths().SettingsPath
            : Path.GetFullPath(settingsPath);
    }

    public ServerSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return ServerSettings.CreateDefault();
            }

            var value = JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            return value ?? ServerSettings.CreateDefault();
        }
        catch
        {
            return ServerSettings.CreateDefault();
        }
    }

    public void Save(ServerSettings settings)
    {
        var paths = new ServerPaths(settings.DataPath);
        paths.EnsureDirectories();

        var normalized = settings with
        {
            PublicUrl = settings.PublicUrl.Trim().TrimEnd('/'),
            DataPath = paths.DataRoot,
            CloudflaredPath = Path.GetFullPath(settings.CloudflaredPath.Trim())
        };

        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    public static string? Validate(ServerSettings settings)
    {
        if (!Uri.TryCreate(settings.PublicUrl, UriKind.Absolute, out var publicUri) ||
            publicUri.Scheme != Uri.UriSchemeHttps)
        {
            return "Die öffentliche URL muss eine gültige HTTPS-Adresse sein.";
        }

        if (string.IsNullOrWhiteSpace(settings.DataPath))
        {
            return "Der Datenpfad darf nicht leer sein.";
        }

        if (string.IsNullOrWhiteSpace(settings.CloudflaredPath) || !File.Exists(settings.CloudflaredPath))
        {
            return "cloudflared.exe wurde am konfigurierten Pfad nicht gefunden.";
        }

        return null;
    }
}
