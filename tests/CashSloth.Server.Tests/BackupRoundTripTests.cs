using CashSloth.Contracts;
using CashSloth.Server.Data;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Tests;

public sealed class BackupRoundTripTests
{
    [Fact]
    public async Task PortableBackup_RestoresDatabaseKeysAndTunnelToken()
    {
        var context = await TestServerContext.CreateAsync();
        var root = context.Root;
        var portablePath = Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests", $"{Guid.NewGuid():N}.cashsloth-server-backup");
        var settings = new ServerSettings
        {
            PublicUrl = "https://api.example.test",
            DataPath = root,
            CloudflaredPath = Path.Combine(root, "cloudflared.exe")
        };
        var settingsStore = new ServerSettingsStore(Path.Combine(root, "settings.json"));
        var paths = context.Paths;
        string fingerprint;

        using (var scope = context.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            await accounts.CreateFirstAdminAsync("owner", "Very-Strong-Password-42!");
            await scope.ServiceProvider.GetRequiredService<PresetService>()
                .CreateAsync(PresetServiceTests.CreatePreset("BACKUP", "Backup"), "test", CancellationToken.None);
        }
        fingerprint = context.GetRequiredService<ServerKeyService>().Fingerprint;
        new TunnelTokenStore(paths).Save("test-tunnel-token-never-log");
        var backupService = new BackupService(paths, settings, settingsStore);
        await backupService.CreatePortableBackupAsync(portablePath, "correct horse battery staple");
        await context.DisposeAsync();

        try
        {
            var restore = new BackupService(paths, settings, settingsStore);
            await restore.RestorePortableBackupAsync(portablePath, "correct horse battery staple", serverIsStopped: true);

            await using var db = new ServerDbContext(DatabaseBootstrapper.CreateOptions(paths.DatabasePath));
            await DatabaseBootstrapper.InitializeAsync(db);
            Assert.Single(await db.Users.ToListAsync());
            Assert.Equal("BACKUP", (await db.Presets.SingleAsync()).Id);
            using var restoredKeys = new ServerKeyService(paths);
            Assert.Equal(fingerprint, restoredKeys.Fingerprint);
            Assert.Equal("test-tunnel-token-never-log", new TunnelTokenStore(paths).Read());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(portablePath)) File.Delete(portablePath);
            var fullRoot = Path.GetFullPath(root);
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests"));
            if (fullRoot.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}
