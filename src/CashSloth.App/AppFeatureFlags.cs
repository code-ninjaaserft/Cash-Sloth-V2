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
    bool ShowStartupAnimation,
    bool KeepLaptopAwake,
    bool KioskMode,
    bool RequireKioskExitPassword,
    bool LockWindowsOnExit)
{
    internal static AppFeatureFlags Full { get; } = new(
        "full",
        ShowPresets: true,
        ShowAccounts: true,
        ShowEvent: true,
        ShowCustomerDisplay: true,
        ShowCatalogEditing: true,
        ShowOnboarding: true,
        ShowStartupAnimation: true,
        KeepLaptopAwake: false,
        KioskMode: false,
        RequireKioskExitPassword: false,
        LockWindowsOnExit: false);
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
        var profile = Environment.GetEnvironmentVariable("CASH_SLOTH_PROFILE");
        if (IsFullProfile(profile))
        {
            return AppFeatureFlags.Full;
        }

        var profilePath = ResolveProfilePath(profile);
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CashSloth",
            FileName);
        var appPath = Path.Combine(AppContext.BaseDirectory, FileName);

        foreach (var path in new[] { overridePath, profilePath, localPath, appPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            return LoadFromFile(path, AppFeatureFlags.Full);
        }

        return AppFeatureFlags.Full;
    }

    private static bool IsFullProfile(string? profile)
    {
        return string.Equals(profile?.Trim(), "full", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ResolveProfilePath(string? profile)
    {
        var normalized = profile?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || IsFullProfile(normalized))
        {
            return null;
        }

        var safeProfile = string.Concat(normalized.Where(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character == '-' ||
            character == '_' ||
            character == '.'));
        if (string.IsNullOrWhiteSpace(safeProfile))
        {
            return null;
        }

        return Path.Combine(AppContext.BaseDirectory, $"CashSloth.Features.{safeProfile}.json");
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
                document.ShowStartupAnimation ?? fallback.ShowStartupAnimation,
                document.KeepLaptopAwake ?? fallback.KeepLaptopAwake,
                document.KioskMode ?? fallback.KioskMode,
                document.RequireKioskExitPassword ?? fallback.RequireKioskExitPassword,
                document.LockWindowsOnExit ?? fallback.LockWindowsOnExit);
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
    [property: JsonPropertyName("show_startup_animation")] bool? ShowStartupAnimation,
    [property: JsonPropertyName("keep_laptop_awake")] bool? KeepLaptopAwake,
    [property: JsonPropertyName("kiosk_mode")] bool? KioskMode,
    [property: JsonPropertyName("require_kiosk_exit_password")] bool? RequireKioskExitPassword,
    [property: JsonPropertyName("lock_windows_on_exit")] bool? LockWindowsOnExit);
