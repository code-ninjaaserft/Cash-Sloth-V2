using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class EventRegisterStoreTests
{
    [Fact]
    public void CanAddUpdateAndLoadClientRegister()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new EventRegisterStore(Path.Combine(tempDir, "event.registers.json"));
            var first = new EventClientRegister(
                "register-1",
                "Sommerfest",
                "Kasse 2",
                "tablet-a",
                "192.168.1.22:43782",
                DateTimeOffset.UtcNow);

            Assert.True(store.TryUpsert(first, out var firstError), firstError);
            Assert.True(store.TryLoad(out var loadedFirst, out var loadFirstError), loadFirstError);
            var loaded = Assert.Single(loadedFirst);
            Assert.Equal("Kasse 2", loaded.RegisterName);

            var updated = first with { RegisterName = "Kasse Bar" };
            Assert.True(store.TryUpsert(updated, out var updateError), updateError);
            Assert.True(store.TryLoad(out var loadedUpdated, out var loadUpdatedError), loadUpdatedError);
            var reloaded = Assert.Single(loadedUpdated);
            Assert.Equal("Kasse Bar", reloaded.RegisterName);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CanRemoveClientRegister()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new EventRegisterStore(Path.Combine(tempDir, "event.registers.json"));
            var register = new EventClientRegister(
                "register-1",
                "Sommerfest",
                "Kasse 2",
                "tablet-a",
                "192.168.1.22:43782",
                DateTimeOffset.UtcNow);

            Assert.True(store.TryUpsert(register, out var saveError), saveError);
            Assert.True(store.TryRemove(register.Id, out var removeError), removeError);
            Assert.True(store.TryLoad(out var loaded, out var loadError), loadError);
            Assert.Empty(loaded);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "CashSlothEventRegisterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}
