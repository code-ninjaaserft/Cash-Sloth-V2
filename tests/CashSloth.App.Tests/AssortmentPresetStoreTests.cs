using System.Text.Json;
using CashSloth.App;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class AssortmentPresetStoreTests
{
    [Fact]
    public void SavesToSqliteAndLoadsFromSqliteWhenJsonIsMissing()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");
            var store = new AssortmentPresetStore(jsonPath, sqlitePath);

            var catalog = new List<CatalogItemEditor>
            {
                new("COFFEE", "Coffee", 500, "Hot Drinks"),
                new("WATER", "Water", 200, "Soft Drinks")
            };
            var extraCategories = new List<string> { "Specials" };

            var saveOk = store.TrySave(catalog, extraCategories, out var saveError);
            Assert.True(saveOk, saveError);
            Assert.True(File.Exists(sqlitePath));

            // Force load from SQLite path by removing JSON snapshot.
            if (File.Exists(jsonPath))
            {
                File.Delete(jsonPath);
            }

            var loadOk = store.TryLoad(out var loadedCatalog, out var loadedExtraCategories, out var loadError);
            Assert.True(loadOk, loadError);
            Assert.Equal(2, loadedCatalog.Count);
            Assert.Contains(loadedCatalog, item => item.Id == "COFFEE" && item.UnitCents == 500);
            Assert.Contains(loadedCatalog, item => item.Id == "WATER" && item.UnitCents == 200);
            Assert.Contains("Specials", loadedExtraCategories);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ImportsLegacyJsonToSqliteWhenSqliteDoesNotExist()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");

            var legacyDocument = new AssortmentStoreDocument(
                1,
                "default",
                new[]
                {
                    new AssortmentPresetDocument(
                        "default",
                        "Default Sortiment",
                        new[] { "Hot Drinks", "Soft Drinks" },
                        new[]
                        {
                            new AssortmentPresetItemDocument("COFFEE", "Coffee", 500, "Hot Drinks"),
                            new AssortmentPresetItemDocument("WATER", "Water", 200, "Soft Drinks")
                        })
                });

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(legacyDocument));
            Assert.False(File.Exists(sqlitePath));

            var store = new AssortmentPresetStore(jsonPath, sqlitePath);
            var loadOk = store.TryLoad(out var loadedCatalog, out var loadedExtraCategories, out var loadError);
            Assert.True(loadOk, loadError);
            Assert.Equal(2, loadedCatalog.Count);
            Assert.Empty(loadedExtraCategories);
            Assert.True(File.Exists(sqlitePath));

            // Validate data remains available without legacy JSON.
            File.Delete(jsonPath);
            var sqliteOnlyStore = new AssortmentPresetStore(jsonPath, sqlitePath);
            var sqliteLoadOk = sqliteOnlyStore.TryLoad(out var sqliteLoadedCatalog, out _, out var sqliteLoadError);
            Assert.True(sqliteLoadOk, sqliteLoadError);
            Assert.Equal(2, sqliteLoadedCatalog.Count);
            Assert.Contains(sqliteLoadedCatalog, item => item.Id == "COFFEE");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FallsBackToJsonWhenSqliteSchemaIsNewerThanSupported()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");

            CreateSqliteDatabaseWithUserVersion(sqlitePath, 2);
            var legacyDocument = BuildLegacyDocument();
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(legacyDocument));

            var store = new AssortmentPresetStore(jsonPath, sqlitePath);
            var loadOk = store.TryLoad(out var loadedCatalog, out var loadedExtraCategories, out var loadError);
            Assert.True(loadOk, loadError);
            Assert.Equal(2, loadedCatalog.Count);
            Assert.Empty(loadedExtraCategories);
            Assert.Contains(loadedCatalog, item => item.Id == "COFFEE" && item.UnitCents == 500);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ReturnsClearErrorWhenOnlySqliteSchemaIsNewerThanSupported()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");

            CreateSqliteDatabaseWithUserVersion(sqlitePath, 2);

            var store = new AssortmentPresetStore(jsonPath, sqlitePath);
            var loadOk = store.TryLoad(out _, out _, out var loadError);
            Assert.False(loadOk);
            Assert.NotNull(loadError);
            Assert.Contains("Unsupported SQLite schema version", loadError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void SaveFailsWhenSqliteSchemaIsNewerThanSupported()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");
            CreateSqliteDatabaseWithUserVersion(sqlitePath, 2);

            var store = new AssortmentPresetStore(jsonPath, sqlitePath);
            var catalog = new List<CatalogItemEditor>
            {
                new("COFFEE", "Coffee", 500, "Hot Drinks")
            };

            var saveOk = store.TrySave(catalog, Array.Empty<string>(), out var saveError);
            Assert.False(saveOk);
            Assert.NotNull(saveError);
            Assert.Contains("Unsupported SQLite schema version", saveError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CanCreateAndSwitchBetweenLocalPresets()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");
            var store = new AssortmentPresetStore(jsonPath, sqlitePath);

            var defaultCatalog = new List<CatalogItemEditor>
            {
                new("COFFEE", "Coffee", 500, "Hot Drinks")
            };

            Assert.True(store.TrySave(defaultCatalog, Array.Empty<string>(), out var initialSaveError), initialSaveError);

            var nightCatalog = new List<CatalogItemEditor>
            {
                new("LATTE", "Latte", 620, "Night Menu"),
                new("BROWNIE", "Brownie", 390, "Night Menu")
            };

            Assert.True(store.TryUpsertPreset("night_menu", "Night Menu", nightCatalog, Array.Empty<string>(), false, out var upsertError), upsertError);
            Assert.True(store.TryGetPresetSummaries(out var summaries, out var summaryError), summaryError);
            Assert.Equal(2, summaries.Count);
            Assert.Contains(summaries, summary => summary.Id == "NIGHT_MENU");

            Assert.True(store.TrySetActivePreset("night_menu", out var switchError), switchError);
            Assert.True(store.TryLoad(out var loadedCatalog, out _, out var loadError), loadError);
            Assert.Equal(2, loadedCatalog.Count);
            Assert.Contains(loadedCatalog, item => item.Id == "LATTE" && item.UnitCents == 620);
            Assert.Contains(loadedCatalog, item => item.Id == "BROWNIE" && item.UnitCents == 390);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void DeletePresetFallsBackToRemainingActivePreset()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jsonPath = Path.Combine(tempDir, "assortment.presets.json");
            var sqlitePath = Path.Combine(tempDir, "assortment.presets.sqlite3");
            var store = new AssortmentPresetStore(jsonPath, sqlitePath);

            var defaultCatalog = new List<CatalogItemEditor>
            {
                new("COFFEE", "Coffee", 500, "Hot Drinks")
            };

            Assert.True(store.TrySave(defaultCatalog, Array.Empty<string>(), out var initialSaveError), initialSaveError);

            var weekendCatalog = new List<CatalogItemEditor>
            {
                new("WAFFLE", "Waffle", 700, "Weekend")
            };

            Assert.True(store.TryUpsertPreset("weekend", "Weekend", weekendCatalog, Array.Empty<string>(), true, out var upsertError), upsertError);
            Assert.True(store.TryDeletePreset("weekend", out var deleteError), deleteError);
            Assert.True(store.TryLoad(out var loadedCatalog, out _, out var loadError), loadError);
            Assert.Single(loadedCatalog);
            Assert.Contains(loadedCatalog, item => item.Id == "COFFEE");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static AssortmentStoreDocument BuildLegacyDocument()
    {
        return new AssortmentStoreDocument(
            1,
            "default",
            new[]
            {
                new AssortmentPresetDocument(
                    "default",
                    "Default Sortiment",
                    new[] { "Hot Drinks", "Soft Drinks" },
                    new[]
                    {
                        new AssortmentPresetItemDocument("COFFEE", "Coffee", 500, "Hot Drinks"),
                        new AssortmentPresetItemDocument("WATER", "Water", 200, "Soft Drinks")
                    })
            });
    }

    private static void CreateSqliteDatabaseWithUserVersion(string sqlitePath, int userVersion)
    {
        var directory = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {userVersion};";
        command.ExecuteNonQuery();
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "CashSlothAppTests", Guid.NewGuid().ToString("N"));
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
