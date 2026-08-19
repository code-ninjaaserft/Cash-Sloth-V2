using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class AppFeatureConfigurationTests
{
    [Fact]
    public void MissingFeatureFileFallsBackToFull()
    {
        var tempDir = CreateTempDir();
        try
        {
            var flags = AppFeatureConfiguration.LoadFromFile(
                Path.Combine(tempDir, "missing.json"),
                AppFeatureFlags.Full);

            Assert.Equal("full", flags.Profile);
            Assert.True(flags.ShowPresets);
            Assert.True(flags.ShowAccounts);
            Assert.True(flags.ShowEvent);
            Assert.True(flags.ShowCustomerDisplay);
            Assert.True(flags.ShowCatalogEditing);
            Assert.True(flags.ShowOnboarding);
            Assert.True(flags.ShowStartupAnimation);
            Assert.False(flags.KeepLaptopAwake);
            Assert.False(flags.KioskMode);
            Assert.False(flags.RequireKioskExitPassword);
            Assert.False(flags.LockWindowsOnExit);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ZammeAesseFeatureFileCanHideUnneededFeatures()
    {
        var tempDir = CreateTempDir();
        try
        {
            var path = Path.Combine(tempDir, "CashSloth.Features.json");
            File.WriteAllText(path, """
                {
                  "profile": "zamme-aesse",
                  "show_presets": false,
                  "show_accounts": false,
                  "show_event": false,
                  "show_customer_display": false,
                  "show_catalog_editing": true,
                  "show_onboarding": false,
                  "show_startup_animation": true,
                  "keep_laptop_awake": true,
                  "kiosk_mode": true,
                  "require_kiosk_exit_password": true,
                  "lock_windows_on_exit": true
                }
                """);

            var flags = AppFeatureConfiguration.LoadFromFile(path, AppFeatureFlags.Full);

            Assert.Equal("zamme-aesse", flags.Profile);
            Assert.False(flags.ShowPresets);
            Assert.False(flags.ShowAccounts);
            Assert.False(flags.ShowEvent);
            Assert.False(flags.ShowCustomerDisplay);
            Assert.True(flags.ShowCatalogEditing);
            Assert.False(flags.ShowOnboarding);
            Assert.True(flags.ShowStartupAnimation);
            Assert.True(flags.KeepLaptopAwake);
            Assert.True(flags.KioskMode);
            Assert.True(flags.RequireKioskExitPassword);
            Assert.True(flags.LockWindowsOnExit);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ProfilePathUsesSanitizedProfileName()
    {
        var path = AppFeatureConfiguration.ResolveProfilePath(" zamme-aesse!! ");

        Assert.NotNull(path);
        Assert.EndsWith("CashSloth.Features.zamme-aesse.json", path, StringComparison.Ordinal);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashsloth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for test temp directories.
        }
    }
}
