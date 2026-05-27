using System.IO;
using Microsoft.Data.Sqlite;

namespace CashSloth.App;

internal sealed record SaleHistoryLine(
    string ItemId,
    string Name,
    long UnitCents,
    int Quantity,
    long LineTotalCents);

internal sealed record SaleHistoryRecord(
    string Id,
    DateTimeOffset CompletedUtc,
    string EventName,
    string RegisterName,
    string OperatorUsername,
    string PaymentMethod,
    bool IsShowcase,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long GivenCents,
    long ChangeCents,
    IReadOnlyList<SaleHistoryLine> Lines);

internal sealed record SaleHistorySummary(
    string Id,
    DateTimeOffset CompletedUtc,
    string EventName,
    string RegisterName,
    string OperatorUsername,
    string PaymentMethod,
    bool IsShowcase,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long GivenCents,
    long ChangeCents,
    int LineCount);

internal sealed record SaleHistoryFilter(
    string? EventName = null,
    string? RegisterName = null,
    string? OperatorUsername = null,
    bool IncludeShowcase = false);

internal sealed record SaleStatistics(
    long SaleCount,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long GivenCents,
    long ChangeCents,
    long LineCount);

internal sealed class SaleHistorySqliteStore
{
    private const int CurrentSchemaVersion = 1;

    internal SaleHistorySqliteStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = Path.Combine(localAppData, "CashSloth", "sales.sqlite3");
    }

    internal SaleHistorySqliteStore(string filePath)
    {
        FilePath = filePath;
    }

    internal string FilePath { get; }

    internal bool TryEnsureInitialized(out string? error)
    {
        error = null;

        try
        {
            using var connection = OpenConnection();
            return TryEnsureSchema(connection, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryRecordSale(SaleHistoryRecord sale, out string saleId, out string? error)
    {
        saleId = string.IsNullOrWhiteSpace(sale.Id) ? Guid.NewGuid().ToString("N") : sale.Id.Trim();
        error = null;

        if (sale.Lines.Count == 0)
        {
            error = "Sale must contain at least one line.";
            return false;
        }

        if (sale.SubtotalCents <= 0 || sale.TotalCents <= 0)
        {
            error = "Sale total must be greater than 0.";
            return false;
        }

        if (sale.TipCents < 0)
        {
            error = "Tip cannot be negative.";
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            using var transaction = connection.BeginTransaction();
            InsertSale(connection, transaction, sale with { Id = saleId });
            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListRecentSales(int limit, bool includeShowcase, out List<SaleHistorySummary> summaries, out string? error)
    {
        summaries = new List<SaleHistorySummary>();
        error = null;

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT id,
                       completed_utc,
                       event_name,
                       register_name,
                       operator_username,
                       payment_method,
                       is_showcase,
                       subtotal_cents,
                       tip_cents,
                       total_cents,
                       given_cents,
                       change_cents,
                       line_count
                FROM sales
                WHERE $include_showcase = 1 OR is_showcase = 0
                ORDER BY completed_utc DESC
                LIMIT $limit;";
            command.Parameters.AddWithValue("$include_showcase", includeShowcase ? 1 : 0);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                summaries.Add(ReadSummary(reader));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryGetStatistics(SaleHistoryFilter filter, out SaleStatistics statistics, out string? error)
    {
        statistics = new SaleStatistics(0, 0, 0, 0, 0, 0, 0);
        error = null;

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*),
                       COALESCE(SUM(subtotal_cents), 0),
                       COALESCE(SUM(tip_cents), 0),
                       COALESCE(SUM(total_cents), 0),
                       COALESCE(SUM(given_cents), 0),
                       COALESCE(SUM(change_cents), 0),
                       COALESCE(SUM(line_count), 0)
                FROM sales
                WHERE ($include_showcase = 1 OR is_showcase = 0)
                  AND ($event_name = '' OR event_name = $event_name COLLATE NOCASE)
                  AND ($register_name = '' OR register_name = $register_name COLLATE NOCASE)
                  AND ($operator_username = '' OR operator_username = $operator_username COLLATE NOCASE);";
            command.Parameters.AddWithValue("$include_showcase", filter.IncludeShowcase ? 1 : 0);
            command.Parameters.AddWithValue("$event_name", NormalizeOptionalFilter(filter.EventName));
            command.Parameters.AddWithValue("$register_name", NormalizeOptionalFilter(filter.RegisterName));
            command.Parameters.AddWithValue("$operator_username", NormalizeOptionalFilter(filter.OperatorUsername));

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                statistics = new SaleStatistics(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6));
            }

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

    private static bool TryEnsureSchema(SqliteConnection connection, out string? error)
    {
        error = null;

        var userVersion = ReadUserVersion(connection);
        if (userVersion > CurrentSchemaVersion)
        {
            error = $"Unsupported sales schema version {userVersion}.";
            return false;
        }

        if (userVersion == 0)
        {
            ExecuteNonQuery(connection, null, @"
                CREATE TABLE IF NOT EXISTS sales (
                    id TEXT PRIMARY KEY,
                    completed_utc TEXT NOT NULL,
                    event_name TEXT NOT NULL,
                    register_name TEXT NOT NULL,
                    operator_username TEXT NOT NULL,
                    payment_method TEXT NOT NULL,
                    is_showcase INTEGER NOT NULL,
                    subtotal_cents INTEGER NOT NULL,
                    tip_cents INTEGER NOT NULL,
                    total_cents INTEGER NOT NULL,
                    given_cents INTEGER NOT NULL,
                    change_cents INTEGER NOT NULL,
                    line_count INTEGER NOT NULL,
                    created_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sale_lines (
                    sale_id TEXT NOT NULL,
                    line_index INTEGER NOT NULL,
                    item_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    unit_cents INTEGER NOT NULL,
                    quantity INTEGER NOT NULL,
                    line_total_cents INTEGER NOT NULL,
                    PRIMARY KEY (sale_id, line_index),
                    FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_sales_completed_utc ON sales(completed_utc);
                CREATE INDEX IF NOT EXISTS ix_sales_event_register_user ON sales(event_name, register_name, operator_username);
                CREATE INDEX IF NOT EXISTS ix_sales_showcase ON sales(is_showcase);
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

    private static void InsertSale(SqliteConnection connection, SqliteTransaction transaction, SaleHistoryRecord sale)
    {
        var completedUtc = sale.CompletedUtc == default ? DateTimeOffset.UtcNow : sale.CompletedUtc.ToUniversalTime();
        var createdUtc = DateTimeOffset.UtcNow.ToString("O");

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO sales (
                    id,
                    completed_utc,
                    event_name,
                    register_name,
                    operator_username,
                    payment_method,
                    is_showcase,
                    subtotal_cents,
                    tip_cents,
                    total_cents,
                    given_cents,
                    change_cents,
                    line_count,
                    created_utc)
                VALUES (
                    $id,
                    $completed_utc,
                    $event_name,
                    $register_name,
                    $operator_username,
                    $payment_method,
                    $is_showcase,
                    $subtotal_cents,
                    $tip_cents,
                    $total_cents,
                    $given_cents,
                    $change_cents,
                    $line_count,
                    $created_utc);";
            command.Parameters.AddWithValue("$id", sale.Id);
            command.Parameters.AddWithValue("$completed_utc", completedUtc.ToString("O"));
            command.Parameters.AddWithValue("$event_name", NormalizeRequiredText(sale.EventName, "Default Event"));
            command.Parameters.AddWithValue("$register_name", NormalizeRequiredText(sale.RegisterName, "Register 1"));
            command.Parameters.AddWithValue("$operator_username", NormalizeRequiredText(sale.OperatorUsername, "local"));
            command.Parameters.AddWithValue("$payment_method", NormalizeRequiredText(sale.PaymentMethod, "Cash"));
            command.Parameters.AddWithValue("$is_showcase", sale.IsShowcase ? 1 : 0);
            command.Parameters.AddWithValue("$subtotal_cents", sale.SubtotalCents);
            command.Parameters.AddWithValue("$tip_cents", sale.TipCents);
            command.Parameters.AddWithValue("$total_cents", sale.TotalCents);
            command.Parameters.AddWithValue("$given_cents", sale.GivenCents);
            command.Parameters.AddWithValue("$change_cents", sale.ChangeCents);
            command.Parameters.AddWithValue("$line_count", sale.Lines.Count);
            command.Parameters.AddWithValue("$created_utc", createdUtc);
            command.ExecuteNonQuery();
        }

        for (var index = 0; index < sale.Lines.Count; index++)
        {
            var line = sale.Lines[index];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO sale_lines (
                    sale_id,
                    line_index,
                    item_id,
                    name,
                    unit_cents,
                    quantity,
                    line_total_cents)
                VALUES (
                    $sale_id,
                    $line_index,
                    $item_id,
                    $name,
                    $unit_cents,
                    $quantity,
                    $line_total_cents);";
            command.Parameters.AddWithValue("$sale_id", sale.Id);
            command.Parameters.AddWithValue("$line_index", index);
            command.Parameters.AddWithValue("$item_id", NormalizeRequiredText(line.ItemId, "UNKNOWN"));
            command.Parameters.AddWithValue("$name", NormalizeRequiredText(line.Name, line.ItemId));
            command.Parameters.AddWithValue("$unit_cents", line.UnitCents);
            command.Parameters.AddWithValue("$quantity", line.Quantity);
            command.Parameters.AddWithValue("$line_total_cents", line.LineTotalCents);
            command.ExecuteNonQuery();
        }
    }

    private static SaleHistorySummary ReadSummary(SqliteDataReader reader)
    {
        var completedUtc = DateTimeOffset.TryParse(reader.GetString(1), out var parsedCompletedUtc)
            ? parsedCompletedUtc
            : DateTimeOffset.MinValue;

        return new SaleHistorySummary(
            reader.GetString(0),
            completedUtc,
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6) != 0,
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            (int)reader.GetInt64(12));
    }

    private static string NormalizeRequiredText(string? text, string fallback)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeOptionalFilter(string? text)
    {
        return text?.Trim() ?? string.Empty;
    }
}
