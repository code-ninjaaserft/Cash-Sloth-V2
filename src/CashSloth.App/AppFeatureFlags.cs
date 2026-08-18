using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal sealed record AppFeatureFlags(
    string Profile,
    bool ShowPresets,
    bool ShowAccounts,
    bool ShowEvent,
    bool ShowCustomerDisplay,
    bool ShowCatalogEditing,
    bool ShowOnboarding,
    bool ShowStartupAnimation)
{
    internal static AppFeatureFlags Full { get; } = new(
        "full",
        ShowPresets: true,
        ShowAccounts: true,
        ShowEvent: true,
        ShowCustomerDisplay: true,
        ShowCatalogEditing: true,
        ShowOnboarding: true,
        ShowStartupAnimation: true);
}

internal static class AppFeatureConfiguration
{
    private const string FileName = "CashSloth.Features.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static AppFeatureFlags Load()
    {
        var overridePath = Environment.GetEnvironmentVariable("CASH_SLOTH_FEATURES");
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CashSloth",
            FileName);
        var appPath = Path.Combine(AppContext.BaseDirectory, FileName);

        foreach (var path in new[] { overridePath, localPath, appPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            return LoadFromFile(path, AppFeatureFlags.Full);
        }

        return AppFeatureFlags.Full;
    }

    internal static AppFeatureFlags LoadFromFile(string filePath, AppFeatureFlags fallback)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var document = JsonSerializer.Deserialize<AppFeatureFlagsDocument>(json, JsonOptions);
            if (document == null)
            {
                return fallback;
            }

            return new AppFeatureFlags(
                NormalizeProfile(document.Profile, fallback.Profile),
                document.ShowPresets ?? fallback.ShowPresets,
                document.ShowAccounts ?? fallback.ShowAccounts,
                document.ShowEvent ?? fallback.ShowEvent,
                document.ShowCustomerDisplay ?? fallback.ShowCustomerDisplay,
                document.ShowCatalogEditing ?? fallback.ShowCatalogEditing,
                document.ShowOnboarding ?? fallback.ShowOnboarding,
                document.ShowStartupAnimation ?? fallback.ShowStartupAnimation);
        }
        catch
        {
            return fallback;
        }
    }

    private static string NormalizeProfile(string? profile, string fallback)
    {
        var normalized = profile?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}

internal sealed record AppFeatureFlagsDocument(
    [property: JsonPropertyName("profile")] string? Profile,
    [property: JsonPropertyName("show_presets")] bool? ShowPresets,
    [property: JsonPropertyName("show_accounts")] bool? ShowAccounts,
    [property: JsonPropertyName("show_event")] bool? ShowEvent,
    [property: JsonPropertyName("show_customer_display")] bool? ShowCustomerDisplay,
    [property: JsonPropertyName("show_catalog_editing")] bool? ShowCatalogEditing,
    [property: JsonPropertyName("show_onboarding")] bool? ShowOnboarding,
    [property: JsonPropertyName("show_startup_animation")] bool? ShowStartupAnimation);
