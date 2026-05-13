using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CashSloth.PresetApi;

internal enum PresetRepoStatus
{
    Success = 0,
    InvalidInput = 1,
    NotFound = 2,
    Error = 3
}

internal sealed class PresetRepository
{
    private const int CurrentSchemaVersion = 1;
    private static readonly Regex PresetIdRegex = new("[^A-Z0-9]+", RegexOptions.Compiled);

    internal PresetRepository(string filePath)
    {
        FilePath = filePath;
    }

    internal string FilePath { get; }

    internal PresetRepoStatus TryReadStore(out AssortmentStoreDocument? document, out string? error)
    {
        document = null;

        try
        {
            using var connection = OpenConnection();
            var schemaStatus = TryEnsureSchema(connection, out error);
            if (schemaStatus != PresetRepoStatus.Success)
            {
                return schemaStatus;
            }

            var presets = ReadPresets(connection);
            var activePresetId = ResolveActivePresetId(ReadMetadata(connection, "active_preset_id"), presets);
            document = new AssortmentStoreDocument(CurrentSchemaVersion, activePresetId, presets.ToArray());
            error = null;
            return PresetRepoStatus.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return PresetRepoStatus.Error;
        }
    }

    internal PresetRepoStatus TryReadPreset(string presetId, out AssortmentPresetDocument? preset, out string? error)
    {
        preset = null;
        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return PresetRepoStatus.InvalidInput;
        }

        var storeStatus = TryReadStore(out var document, out error);
        if (storeStatus != PresetRepoStatus.Success || document == null)
        {
            return storeStatus;
        }

        preset = document.Presets.FirstOrDefault(existing =>
            string.Equals(NormalizePresetId(existing.Id), normalizedPresetId, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            error = $"Preset '{normalizedPresetId}' does not exist.";
            return PresetRepoStatus.NotFound;
        }

        error = null;
        return PresetRepoStatus.Success;
    }

    internal PresetRepoStatus TryUpsertPreset(AssortmentPresetDocument preset, bool setActive, out string persistedPresetId, out string? error)
    {
        persistedPresetId = string.Empty;

        var normalizeStatus = TryNormalizePreset(preset, out var normalizedPreset, out error);
        if (normalizeStatus != PresetRepoStatus.Success)
        {
            return normalizeStatus;
        }

        persistedPresetId = normalizedPreset.Id;

        try
        {
            using var connection = OpenConnection();
            var schemaStatus = TryEnsureSchema(connection, out error);
            if (schemaStatus != PresetRepoStatus.Success)
            {
                return schemaStatus;
            }

            using var transaction = connection.BeginTransaction();

            using (var presetUpsert = connection.CreateCommand())
            {
                presetUpsert.Transaction = transaction;
                presetUpsert.CommandText = @"
                    INSERT INTO presets (id, name)
                    VALUES ($id, $name)
                    ON CONFLICT(id) DO UPDATE SET name = excluded.name;";
                presetUpsert.Parameters.AddWithValue("$id", normalizedPreset.Id);
                presetUpsert.Parameters.AddWithValue("$name", normalizedPreset.Name);
                presetUpsert.ExecuteNonQuery();
            }

            using (var deleteCategories = connection.CreateCommand())
            {
                deleteCategories.Transaction = transaction;
                deleteCategories.CommandText = "DELETE FROM preset_categories WHERE preset_id = $preset_id;";
                deleteCategories.Parameters.AddWithValue("$preset_id", normalizedPreset.Id);
                deleteCategories.ExecuteNonQuery();
            }

            using (var deleteItems = connection.CreateCommand())
            {
                deleteItems.Transaction = transaction;
                deleteItems.CommandText = "DELETE FROM preset_items WHERE preset_id = $preset_id;";
                deleteItems.Parameters.AddWithValue("$preset_id", normalizedPreset.Id);
                deleteItems.ExecuteNonQuery();
            }

            foreach (var category in normalizedPreset.Categories)
            {
                using var categoryInsert = connection.CreateCommand();
                categoryInsert.Transaction = transaction;
                categoryInsert.CommandText = "INSERT INTO preset_categories (preset_id, category) VALUES ($preset_id, $category);";
                categoryInsert.Parameters.AddWithValue("$preset_id", normalizedPreset.Id);
                categoryInsert.Parameters.AddWithValue("$category", category);
                categoryInsert.ExecuteNonQuery();
            }

            foreach (var item in normalizedPreset.Items)
            {
                using var itemInsert = connection.CreateCommand();
                itemInsert.Transaction = transaction;
                itemInsert.CommandText = @"
                    INSERT INTO preset_items (preset_id, id, name, unit_cents, category)
                    VALUES ($preset_id, $id, $name, $unit_cents, $category);";
                itemInsert.Parameters.AddWithValue("$preset_id", normalizedPreset.Id);
                itemInsert.Parameters.AddWithValue("$id", item.Id);
                itemInsert.Parameters.AddWithValue("$name", item.Name);
                itemInsert.Parameters.AddWithValue("$unit_cents", item.UnitCents);
                itemInsert.Parameters.AddWithValue("$category", item.Category);
                itemInsert.ExecuteNonQuery();
            }

            var currentActivePresetId = NormalizePresetId(ReadMetadata(connection, "active_preset_id", transaction));
            if (setActive || string.IsNullOrWhiteSpace(currentActivePresetId))
            {
                UpsertMetadata(connection, transaction, "active_preset_id", normalizedPreset.Id);
            }

            UpsertMetadata(connection, transaction, "schema_version", CurrentSchemaVersion.ToString());

            transaction.Commit();
            error = null;
            return PresetRepoStatus.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return PresetRepoStatus.Error;
        }
    }

    internal PresetRepoStatus TrySetActivePreset(string presetId, out string? error)
    {
        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return PresetRepoStatus.InvalidInput;
        }

        try
        {
            using var connection = OpenConnection();
            var schemaStatus = TryEnsureSchema(connection, out error);
            if (schemaStatus != PresetRepoStatus.Success)
            {
                return schemaStatus;
            }

            using var transaction = connection.BeginTransaction();
            if (!PresetExists(connection, transaction, normalizedPresetId))
            {
                transaction.Rollback();
                error = $"Preset '{normalizedPresetId}' does not exist.";
                return PresetRepoStatus.NotFound;
            }

            UpsertMetadata(connection, transaction, "active_preset_id", normalizedPresetId);
            transaction.Commit();
            error = null;
            return PresetRepoStatus.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return PresetRepoStatus.Error;
        }
    }

    internal PresetRepoStatus TryDeletePreset(string presetId, out string? error)
    {
        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return PresetRepoStatus.InvalidInput;
        }

        try
        {
            using var connection = OpenConnection();
            var schemaStatus = TryEnsureSchema(connection, out error);
            if (schemaStatus != PresetRepoStatus.Success)
            {
                return schemaStatus;
            }

            using var transaction = connection.BeginTransaction();
            if (!PresetExists(connection, transaction, normalizedPresetId))
            {
                transaction.Rollback();
                error = $"Preset '{normalizedPresetId}' does not exist.";
                return PresetRepoStatus.NotFound;
            }

            using (var deleteItems = connection.CreateCommand())
            {
                deleteItems.Transaction = transaction;
                deleteItems.CommandText = "DELETE FROM preset_items WHERE preset_id = $preset_id;";
                deleteItems.Parameters.AddWithValue("$preset_id", normalizedPresetId);
                deleteItems.ExecuteNonQuery();
            }

            using (var deleteCategories = connection.CreateCommand())
            {
                deleteCategories.Transaction = transaction;
                deleteCategories.CommandText = "DELETE FROM preset_categories WHERE preset_id = $preset_id;";
                deleteCategories.Parameters.AddWithValue("$preset_id", normalizedPresetId);
                deleteCategories.ExecuteNonQuery();
            }

            using (var deletePreset = connection.CreateCommand())
            {
                deletePreset.Transaction = transaction;
                deletePreset.CommandText = "DELETE FROM presets WHERE id = $id;";
                deletePreset.Parameters.AddWithValue("$id", normalizedPresetId);
                deletePreset.ExecuteNonQuery();
            }

            var activePresetId = NormalizePresetId(ReadMetadata(connection, "active_preset_id", transaction));
            if (string.Equals(activePresetId, normalizedPresetId, StringComparison.OrdinalIgnoreCase))
            {
                var fallbackPresetId = ReadFirstPresetId(connection, transaction);
                if (string.IsNullOrWhiteSpace(fallbackPresetId))
                {
                    DeleteMetadata(connection, transaction, "active_preset_id");
                }
                else
                {
                    UpsertMetadata(connection, transaction, "active_preset_id", fallbackPresetId);
                }
            }

            transaction.Commit();
            error = null;
            return PresetRepoStatus.Success;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return PresetRepoStatus.Error;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={FilePath}");
        connection.Open();
        return connection;
    }

    private static PresetRepoStatus TryEnsureSchema(SqliteConnection connection, out string? error)
    {
        error = null;
        var userVersion = ReadUserVersion(connection);
        if (userVersion > CurrentSchemaVersion)
        {
            error = $"Unsupported SQLite schema version {userVersion}.";
            return PresetRepoStatus.Error;
        }

        if (userVersion == 0)
        {
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS presets (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS preset_categories (
                    preset_id TEXT NOT NULL,
                    category TEXT NOT NULL,
                    PRIMARY KEY (preset_id, category)
                );

                CREATE TABLE IF NOT EXISTS preset_items (
                    preset_id TEXT NOT NULL,
                    id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    unit_cents INTEGER NOT NULL,
                    category TEXT NOT NULL,
                    PRIMARY KEY (preset_id, id)
                );
            ";
            create.ExecuteNonQuery();

            using var version = connection.CreateCommand();
            version.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            version.ExecuteNonQuery();
        }

        return PresetRepoStatus.Success;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return value is long version ? (int)version : 0;
    }

    private static bool PresetExists(SqliteConnection connection, SqliteTransaction transaction, string presetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM presets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", presetId);
        return command.ExecuteScalar() is long count && count > 0;
    }

    private static List<AssortmentPresetDocument> ReadPresets(SqliteConnection connection)
    {
        var presets = new List<AssortmentPresetDocument>();

        using var presetCommand = connection.CreateCommand();
        presetCommand.CommandText = "SELECT id, name FROM presets ORDER BY name COLLATE NOCASE, id COLLATE NOCASE;";
        using var presetReader = presetCommand.ExecuteReader();
        while (presetReader.Read())
        {
            var presetId = presetReader.GetString(0);
            var presetName = presetReader.GetString(1);
            var categories = ReadCategories(connection, presetId);
            var items = ReadItems(connection, presetId);

            presets.Add(new AssortmentPresetDocument(presetId, presetName, categories.ToArray(), items.ToArray()));
        }

        return presets;
    }

    private static List<string> ReadCategories(SqliteConnection connection, string presetId)
    {
        var categories = new List<string>();

        using var categoryCommand = connection.CreateCommand();
        categoryCommand.CommandText = "SELECT category FROM preset_categories WHERE preset_id = $preset_id ORDER BY category COLLATE NOCASE;";
        categoryCommand.Parameters.AddWithValue("$preset_id", presetId);
        using var categoryReader = categoryCommand.ExecuteReader();
        while (categoryReader.Read())
        {
            categories.Add(categoryReader.GetString(0));
        }

        return categories;
    }

    private static List<AssortmentPresetItemDocument> ReadItems(SqliteConnection connection, string presetId)
    {
        var items = new List<AssortmentPresetItemDocument>();

        using var itemCommand = connection.CreateCommand();
        itemCommand.CommandText = @"
            SELECT id, name, unit_cents, category
            FROM preset_items
            WHERE preset_id = $preset_id
            ORDER BY name COLLATE NOCASE, id COLLATE NOCASE;";
        itemCommand.Parameters.AddWithValue("$preset_id", presetId);
        using var itemReader = itemCommand.ExecuteReader();
        while (itemReader.Read())
        {
            items.Add(new AssortmentPresetItemDocument(
                itemReader.GetString(0),
                itemReader.GetString(1),
                itemReader.GetInt64(2),
                itemReader.GetString(3)));
        }

        return items;
    }

    private static string ResolveActivePresetId(string? requestedActivePresetId, IReadOnlyCollection<AssortmentPresetDocument> presets)
    {
        if (presets.Count == 0)
        {
            return string.Empty;
        }

        var normalizedActivePresetId = NormalizePresetId(requestedActivePresetId);
        if (!string.IsNullOrWhiteSpace(normalizedActivePresetId) &&
            presets.Any(existing => string.Equals(NormalizePresetId(existing.Id), normalizedActivePresetId, StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedActivePresetId;
        }

        return NormalizePresetId(presets.First().Id);
    }

    private static string? ReadMetadata(SqliteConnection connection, string key, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void UpsertMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void DeleteMetadata(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static string ReadFirstPresetId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM presets ORDER BY name COLLATE NOCASE, id COLLATE NOCASE LIMIT 1;";
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static PresetRepoStatus TryNormalizePreset(AssortmentPresetDocument preset, out AssortmentPresetDocument normalizedPreset, out string? error)
    {
        normalizedPreset = new AssortmentPresetDocument(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<AssortmentPresetItemDocument>());

        var normalizedPresetId = NormalizePresetId(preset.Id);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            normalizedPresetId = NormalizePresetId(preset.Name);
        }

        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            normalizedPresetId = "ONLINE_PRESET";
        }

        var normalizedPresetName = string.IsNullOrWhiteSpace(preset.Name)
            ? normalizedPresetId
            : preset.Name.Trim();

        if (preset.Items == null || preset.Items.Length == 0)
        {
            error = "Preset JSON does not contain valid items.";
            return PresetRepoStatus.InvalidInput;
        }

        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in preset.Categories ?? Array.Empty<string>())
        {
            var trimmed = category?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                categories.Add(trimmed);
            }
        }

        var items = new List<AssortmentPresetItemDocument>();
        var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        foreach (var item in preset.Items)
        {
            var normalizedItemId = NormalizePresetId(item.Id);
            if (string.IsNullOrWhiteSpace(normalizedItemId))
            {
                normalizedItemId = NormalizePresetId(item.Name);
            }

            if (string.IsNullOrWhiteSpace(normalizedItemId))
            {
                normalizedItemId = $"ITEM_{index}";
            }

            if (!seenItemIds.Add(normalizedItemId))
            {
                error = $"Preset contains duplicate item id '{normalizedItemId}'.";
                return PresetRepoStatus.InvalidInput;
            }

            if (item.UnitCents < 0)
            {
                error = $"Preset item '{normalizedItemId}' has a negative unit_cents value.";
                return PresetRepoStatus.InvalidInput;
            }

            var normalizedItemName = string.IsNullOrWhiteSpace(item.Name)
                ? normalizedItemId
                : item.Name.Trim();

            var normalizedCategory = string.IsNullOrWhiteSpace(item.Category)
                ? "General"
                : item.Category.Trim();

            categories.Add(normalizedCategory);
            items.Add(new AssortmentPresetItemDocument(normalizedItemId, normalizedItemName, item.UnitCents, normalizedCategory));
            index++;
        }

        if (items.Count == 0)
        {
            error = "Preset JSON does not contain valid items.";
            return PresetRepoStatus.InvalidInput;
        }

        normalizedPreset = new AssortmentPresetDocument(
            normalizedPresetId,
            normalizedPresetName,
            categories
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            items
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        error = null;
        return PresetRepoStatus.Success;
    }

    internal static string NormalizePresetId(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return string.Empty;
        }

        var candidate = PresetIdRegex.Replace(presetId.Trim().ToUpperInvariant(), "_").Trim('_');
        return candidate;
    }
}

internal sealed record AssortmentStoreDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("active_preset_id")] string ActivePresetId,
    [property: JsonPropertyName("presets")] AssortmentPresetDocument[] Presets);

internal sealed record AssortmentPresetDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("categories")] string[] Categories,
    [property: JsonPropertyName("items")] AssortmentPresetItemDocument[] Items);

internal sealed record AssortmentPresetItemDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit_cents")] long UnitCents,
    [property: JsonPropertyName("category")] string Category);
