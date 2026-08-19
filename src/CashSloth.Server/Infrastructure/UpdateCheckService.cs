using System.Net.Http.Json;
using System.Net.Http;
using System.Reflection;

namespace CashSloth.Server.Infrastructure;

public sealed record UpdateManifest(string Version, string DownloadUrl, string? ReleaseNotesUrl);

public sealed record UpdateCheckResult(bool Checked, bool UpdateAvailable, UpdateManifest? Manifest, string? Error);

public static class UpdateCheckService
{
    public static async Task<UpdateCheckResult> CheckOnceDailyAsync(
        ServerSettings settings,
        ServerSettingsStore settingsStore,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl))
        {
            return new UpdateCheckResult(false, false, null, null);
        }
        if (settings.LastUpdateCheckUtc is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1))
        {
            return new UpdateCheckResult(false, false, null, null);
        }
        if (!Uri.TryCreate(settings.UpdateManifestUrl, UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            return new UpdateCheckResult(false, false, null, "Update-Manifest muss über HTTPS geladen werden.");
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var manifest = await client.GetFromJsonAsync<UpdateManifest>(manifestUri, cancellationToken);
            if (manifest is null ||
                !Version.TryParse(manifest.Version, out var remoteVersion) ||
                !Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                return new UpdateCheckResult(true, false, null, "Update-Manifest ist ungültig.");
            }

            settingsStore.Save(settings with { LastUpdateCheckUtc = DateTimeOffset.UtcNow });
            var localVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            return new UpdateCheckResult(true, remoteVersion > localVersion, manifest, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new UpdateCheckResult(true, false, null, exception.Message);
        }
    }
}
