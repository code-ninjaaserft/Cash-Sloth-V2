using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class AuthSqliteStoreTests
{
    [Fact]
    public void SeedsDefaultAdminAndAuthenticates()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sqlitePath = Path.Combine(tempDir, "accounts.sqlite3");
            var store = new AuthSqliteStore(sqlitePath);

            var initialized = store.TryEnsureInitialized(out var seededDefaultAdmin, out var initError);
            Assert.True(initialized, initError);
            Assert.True(seededDefaultAdmin);

            var authOk = store.TryAuthenticate("admin", "admin", out var session, out var authError);
            Assert.True(authOk, authError);
            Assert.NotNull(session);
            Assert.Equal("admin", session!.Username);
            Assert.Equal(UserRole.Admin, session.Role);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CanCreateCreatorAndAuthenticate()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sqlitePath = Path.Combine(tempDir, "accounts.sqlite3");
            var store = new AuthSqliteStore(sqlitePath);
            Assert.True(store.TryEnsureInitialized(out _, out var initError), initError);

            var upsertOk = store.TryUpsertAccount("creator_user", "pw-123", UserRole.Creator, true, out var upsertError);
            Assert.True(upsertOk, upsertError);

            var authOk = store.TryAuthenticate("creator_user", "pw-123", out var session, out var authError);
            Assert.True(authOk, authError);
            Assert.NotNull(session);
            Assert.Equal(UserRole.Creator, session!.Role);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CannotDeleteLastEnabledAdmin()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sqlitePath = Path.Combine(tempDir, "accounts.sqlite3");
            var store = new AuthSqliteStore(sqlitePath);
            Assert.True(store.TryEnsureInitialized(out _, out var initError), initError);

            var deleteOk = store.TryDeleteAccount("admin", out var deleteError);
            Assert.False(deleteOk);
            Assert.NotNull(deleteError);
            Assert.Contains("At least one enabled admin account must remain", deleteError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CanDeleteAdminWhenAnotherEnabledAdminExists()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sqlitePath = Path.Combine(tempDir, "accounts.sqlite3");
            var store = new AuthSqliteStore(sqlitePath);
            Assert.True(store.TryEnsureInitialized(out _, out var initError), initError);

            Assert.True(store.TryUpsertAccount("admin2", "pw-456", UserRole.Admin, true, out var createError), createError);
            Assert.True(store.TryDeleteAccount("admin", out var deleteError), deleteError);

            var authOk = store.TryAuthenticate("admin2", "pw-456", out var session, out var authError);
            Assert.True(authOk, authError);
            Assert.Equal(UserRole.Admin, session!.Role);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LocalAdminBypassAuthenticatesOnSameLaptopWithoutPassword()
    {
        var tempDir = CreateTempDir();
        try
        {
            var sqlitePath = Path.Combine(tempDir, "accounts.sqlite3");
            var store = new AuthSqliteStore(sqlitePath);
            Assert.True(store.TryEnsureInitialized(out _, out var initError), initError);

            var bypassOk = store.TryAuthenticateLocalAdminBypass(out var session, out var bypassError);
            Assert.True(bypassOk, bypassError);
            Assert.NotNull(session);
            Assert.Equal(UserRole.Admin, session!.Role);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "CashSlothAuthTests", Guid.NewGuid().ToString("N"));
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
