using System.IO;
using Microsoft.Data.Sqlite;

namespace CashSloth.App;

internal sealed class AssortmentSqliteStore
{
    private const int CurrentSchemaVersion = 1;

    internal AssortmentSqliteStore(string filePath)
    {
        FilePath = filePath;
    }

    internal string FilePath { get; }

    internal bool TryLoad(out AssortmentStoreDocument? document, out string? error)
    {
        document = null;
        error = null;

        if (!File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            var presets = ReadPresets(connection);
            if (presets.Count == 0)
            {
                return false;
            }

            var activePresetId = ReadMetadata(connection, "active_preset_id");
            if (string.IsNullOrWhiteSpace(activePresetId))
            {
                activePresetId = presets[0].Id;
            }

            document = new AssortmentStoreDocument(
                CurrentSchemaVersion,
                activePresetId,
                presets.ToArray());

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TrySave(AssortmentStoreDocument document, out string? error)
    {
        error = null;

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, transaction, "DELETE FROM preset_items;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM preset_categories;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM presets;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM metadata;");

            InsertMetadata(connection, transaction, "active_preset_id", document.ActivePresetId);
            InsertMetadata(connection, transaction, "schema_version", document.SchemaVersion.ToString());

            foreach (var preset in document.Presets)
            {
                InsertPreset(connection, transaction, preset);
            }

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
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

    private bool TryEnsureSchema(SqliteConnection connection, out string? error)
    {
        error = null;

        var userVersion = ReadUserVersion(connection);
        if (userVersion > CurrentSchemaVersion)
        {
            error = $"Unsupported SQLite schema version {userVersion}.";
            return false;
        }

        if (userVersion == 0)
        {
            ExecuteNonQuery(connection, null, @"
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
            ");

            ExecuteNonQuery(connection, null, $"PRAGMA user_version = {CurrentSchemaVersion};");
        }

        return true;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return value is long version ? (int)version : 0;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void InsertMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void InsertPreset(SqliteConnection connection, SqliteTransaction transaction, AssortmentPresetDocument preset)
    {
        using (var presetCommand = connection.CreateCommand())
        {
            presetCommand.Transaction = transaction;
            presetCommand.CommandText = "INSERT INTO presets (id, name) VALUES ($id, $name);";
            presetCommand.Parameters.AddWithValue("$id", preset.Id);
            presetCommand.Parameters.AddWithValue("$name", preset.Name);
            presetCommand.ExecuteNonQuery();
        }

        foreach (var category in preset.Categories)
        {
            using var categoryCommand = connection.CreateCommand();
            categoryCommand.Transaction = transaction;
            categoryCommand.CommandText = "INSERT INTO preset_categories (preset_id, category) VALUES ($preset_id, $category);";
            categoryCommand.Parameters.AddWithValue("$preset_id", preset.Id);
            categoryCommand.Parameters.AddWithValue("$category", category);
            categoryCommand.ExecuteNonQuery();
        }

        foreach (var item in preset.Items)
        {
            using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = @"
                INSERT INTO preset_items (preset_id, id, name, unit_cents, category)
                VALUES ($preset_id, $id, $name, $unit_cents, $category);";
            itemCommand.Parameters.AddWithValue("$preset_id", preset.Id);
            itemCommand.Parameters.AddWithValue("$id", item.Id);
            itemCommand.Parameters.AddWithValue("$name", item.Name);
            itemCommand.Parameters.AddWithValue("$unit_cents", item.UnitCents);
            itemCommand.Parameters.AddWithValue("$category", item.Category);
            itemCommand.ExecuteNonQuery();
        }
    }

    private static string? ReadMetadata(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static List<AssortmentPresetDocument> ReadPresets(SqliteConnection connection)
    {
        var presets = new List<AssortmentPresetDocument>();

        using var presetCommand = connection.CreateCommand();
        presetCommand.CommandText = "SELECT id, name FROM presets ORDER BY id;";
        using var reader = presetCommand.ExecuteReader();

        while (reader.Read())
        {
            var presetId = reader.GetString(0);
            var presetName = reader.GetString(1);

            var categories = ReadCategories(connection, presetId);
            var items = ReadItems(connection, presetId);

            presets.Add(new AssortmentPresetDocument(
                presetId,
                presetName,
                categories.ToArray(),
                items.ToArray()));
        }

        return presets;
    }

    private static List<string> ReadCategories(SqliteConnection connection, string presetId)
    {
        var categories = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT category FROM preset_categories WHERE preset_id = $preset_id ORDER BY category;";
        command.Parameters.AddWithValue("$preset_id", presetId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            categories.Add(reader.GetString(0));
        }

        return categories;
    }

    private static List<AssortmentPresetItemDocument> ReadItems(SqliteConnection connection, string presetId)
    {
        var items = new List<AssortmentPresetItemDocument>();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, name, unit_cents, category
            FROM preset_items
            WHERE preset_id = $preset_id
            ORDER BY id;";
        command.Parameters.AddWithValue("$preset_id", presetId);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new AssortmentPresetItemDocument(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3)));
        }

        return items;
    }
}
