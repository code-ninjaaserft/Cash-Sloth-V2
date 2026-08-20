using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void KioskModeIsDisabledByDefault()
    {
        Assert.False(AppSettings.Default.KioskModeEnabled);
    }

    [Fact]
    public void KioskModeSettingRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "cashsloth-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "ui.settings.json");

        try
        {
            var store = new AppSettingsStore(path);
            var expected = AppSettings.Default with
            {
                KioskModeEnabled = true,
                KioskExitPasswordHash = "test-hash"
            };

            Assert.True(store.TrySave(expected, out var error), error);

            var loaded = store.Load();
            Assert.True(loaded.KioskModeEnabled);
            Assert.Equal("test-hash", loaded.KioskExitPasswordHash);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void OlderSettingsKeepKioskModeDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "cashsloth-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "ui.settings.json");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, """
                {
                  "schema_version": 3,
                  "language": "GermanCh",
                  "currency": "Chf",
                  "theme": "System",
                  "has_seen_onboarding": false,
                  "kiosk_exit_password_hash": ""
                }
                """);

            var loaded = new AppSettingsStore(path).Load();

            Assert.False(loaded.KioskModeEnabled);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
