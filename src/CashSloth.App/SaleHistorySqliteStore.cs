using System.IO;
using CashSloth.Contracts;
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
    IReadOnlyList<SaleHistoryLine> Lines,
    Guid? ServerEventId = null,
    Guid? EventMemberId = null,
    string? EventNickname = null,
    string? RecordingId = null);

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

internal sealed record PendingEventSale(Guid EventId, Guid MemberId, SaleHistoryRecord Sale);

internal sealed record HistoryRecording(
    string Id,
    string Name,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long SaleCount,
    long TotalCents);

internal sealed record HistoryArchive(
    string Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    long SaleCount,
    long TotalCents);

internal sealed class SaleHistorySqliteStore
{
    private const int CurrentSchemaVersion = 2;

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
                WHERE archive_id IS NULL
                  AND ($include_showcase = 1 OR is_showcase = 0)
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
                WHERE archive_id IS NULL
                  AND ($include_showcase = 1 OR is_showcase = 0)
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
                CREATE TABLE IF NOT EXISTS history_recordings (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    ended_utc TEXT NULL,
                    created_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS history_archives (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    created_utc TEXT NOT NULL
                );

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
                    created_utc TEXT NOT NULL,
                    event_id TEXT NULL,
                    event_member_id TEXT NULL,
                    event_nickname TEXT NULL,
                    recording_id TEXT NULL,
                    archive_id TEXT NULL,
                    sync_status TEXT NOT NULL DEFAULT 'LocalOnly',
                    sync_error TEXT NULL,
                    synced_at_utc TEXT NULL
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
                CREATE INDEX IF NOT EXISTS ix_sales_recording ON sales(recording_id, completed_utc);
                CREATE INDEX IF NOT EXISTS ix_sales_archive ON sales(archive_id, completed_utc);
                CREATE INDEX IF NOT EXISTS ix_sales_event ON sales(event_id, event_member_id, completed_utc);

                CREATE TABLE IF NOT EXISTS event_sale_outbox (
                    sale_id TEXT PRIMARY KEY,
                    event_id TEXT NOT NULL,
                    member_id TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'Pending',
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    last_attempt_utc TEXT NULL,
                    last_error TEXT NULL,
                    FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_event_sale_outbox_status ON event_sale_outbox(status, last_attempt_utc);
            ");

            ExecuteNonQuery(connection, null, $"PRAGMA user_version = {CurrentSchemaVersion};");
        }
        else if (userVersion < CurrentSchemaVersion)
        {
            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, @"
                CREATE TABLE IF NOT EXISTS history_recordings (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    ended_utc TEXT NULL,
                    created_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS history_archives (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    created_utc TEXT NOT NULL
                );
                ALTER TABLE sales ADD COLUMN event_id TEXT NULL;
                ALTER TABLE sales ADD COLUMN event_member_id TEXT NULL;
                ALTER TABLE sales ADD COLUMN event_nickname TEXT NULL;
                ALTER TABLE sales ADD COLUMN recording_id TEXT NULL;
                ALTER TABLE sales ADD COLUMN archive_id TEXT NULL;
                ALTER TABLE sales ADD COLUMN sync_status TEXT NOT NULL DEFAULT 'LocalOnly';
                ALTER TABLE sales ADD COLUMN sync_error TEXT NULL;
                ALTER TABLE sales ADD COLUMN synced_at_utc TEXT NULL;
                CREATE INDEX IF NOT EXISTS ix_sales_recording ON sales(recording_id, completed_utc);
                CREATE INDEX IF NOT EXISTS ix_sales_archive ON sales(archive_id, completed_utc);
                CREATE INDEX IF NOT EXISTS ix_sales_event ON sales(event_id, event_member_id, completed_utc);
                CREATE TABLE IF NOT EXISTS event_sale_outbox (
                    sale_id TEXT PRIMARY KEY,
                    event_id TEXT NOT NULL,
                    member_id TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'Pending',
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    last_attempt_utc TEXT NULL,
                    last_error TEXT NULL,
                    FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_event_sale_outbox_status ON event_sale_outbox(status, last_attempt_utc);
            ");
            ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {CurrentSchemaVersion};");
            transaction.Commit();
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

        var recordingId = string.IsNullOrWhiteSpace(sale.RecordingId)
            ? ReadActiveRecordingId(connection, transaction)
            : sale.RecordingId.Trim();

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
                    created_utc,
                    event_id,
                    event_member_id,
                    event_nickname,
                    recording_id,
                    sync_status)
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
                    $created_utc,
                    $event_id,
                    $event_member_id,
                    $event_nickname,
                    $recording_id,
                    $sync_status);";
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
            command.Parameters.AddWithValue("$event_id", (object?)sale.ServerEventId?.ToString("N") ?? DBNull.Value);
            command.Parameters.AddWithValue("$event_member_id", (object?)sale.EventMemberId?.ToString("N") ?? DBNull.Value);
            command.Parameters.AddWithValue("$event_nickname", (object?)sale.EventNickname?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("$recording_id", (object?)recordingId ?? DBNull.Value);
            command.Parameters.AddWithValue("$sync_status", sale.ServerEventId.HasValue && sale.EventMemberId.HasValue ? "Pending" : "LocalOnly");
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

        if (sale.ServerEventId.HasValue && sale.EventMemberId.HasValue)
        {
            using var outbox = connection.CreateCommand();
            outbox.Transaction = transaction;
            outbox.CommandText = @"
                INSERT INTO event_sale_outbox (sale_id, event_id, member_id, status)
                VALUES ($sale_id, $event_id, $member_id, 'Pending');";
            outbox.Parameters.AddWithValue("$sale_id", sale.Id);
            outbox.Parameters.AddWithValue("$event_id", sale.ServerEventId.Value.ToString("N"));
            outbox.Parameters.AddWithValue("$member_id", sale.EventMemberId.Value.ToString("N"));
            outbox.ExecuteNonQuery();
        }
    }

    internal bool TryGetPendingEventSaleCount(Guid? eventId, out int count, out string? error)
    {
        count = 0;
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM event_sale_outbox
                WHERE status IN ('Pending', 'Rejected') AND ($event_id = '' OR event_id = $event_id);";
            command.Parameters.AddWithValue("$event_id", eventId?.ToString("N") ?? string.Empty);
            count = Convert.ToInt32(command.ExecuteScalar());
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListPendingEventSales(Guid eventId, int limit, out List<PendingEventSale> sales, out string? error)
    {
        sales = [];
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            var ids = new List<(string SaleId, Guid MemberId)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT sale_id, member_id
                    FROM event_sale_outbox
                    WHERE event_id = $event_id AND status = 'Pending'
                    ORDER BY rowid
                    LIMIT $limit;";
                command.Parameters.AddWithValue("$event_id", eventId.ToString("N"));
                command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(1), out var memberId)) ids.Add((reader.GetString(0), memberId));
                }
            }
            foreach (var (saleId, memberId) in ids)
            {
                var sale = ReadSale(connection, saleId);
                if (sale is not null) sales.Add(new PendingEventSale(eventId, memberId, sale));
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryApplyEventSaleSyncResults(
        IReadOnlyList<EventSaleUploadResult> results,
        out string? error)
    {
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var transaction = connection.BeginTransaction();
            foreach (var result in results)
            {
                if (result.Disposition is EventSaleSyncDisposition.Accepted or EventSaleSyncDisposition.Duplicate)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = @"
                        UPDATE sales
                        SET sync_status = 'Synced', sync_error = NULL, synced_at_utc = $synced
                        WHERE id = $id;
                        DELETE FROM event_sale_outbox WHERE sale_id = $id;";
                    update.Parameters.AddWithValue("$id", result.ClientSaleId);
                    update.Parameters.AddWithValue("$synced", (result.AcceptedAtUtc ?? DateTimeOffset.UtcNow).ToString("O"));
                    update.ExecuteNonQuery();
                }
                else
                {
                    using var reject = connection.CreateCommand();
                    reject.Transaction = transaction;
                    reject.CommandText = @"
                        UPDATE sales SET sync_status = 'Rejected', sync_error = $error WHERE id = $id;
                        UPDATE event_sale_outbox SET status = 'Rejected', last_error = $error, last_attempt_utc = $attempt WHERE sale_id = $id;";
                    reject.Parameters.AddWithValue("$id", result.ClientSaleId);
                    reject.Parameters.AddWithValue("$error", result.Message ?? result.ErrorCode ?? "Rejected");
                    reject.Parameters.AddWithValue("$attempt", DateTimeOffset.UtcNow.ToString("O"));
                    reject.ExecuteNonQuery();
                }
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

    internal bool TryMarkEventSyncAttemptFailed(IEnumerable<string> saleIds, string message, out string? error)
    {
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var transaction = connection.BeginTransaction();
            foreach (var saleId in saleIds)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE event_sale_outbox
                    SET attempt_count = attempt_count + 1, last_attempt_utc = $attempt, last_error = $error
                    WHERE sale_id = $id AND status = 'Pending';";
                command.Parameters.AddWithValue("$id", saleId);
                command.Parameters.AddWithValue("$attempt", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$error", message.Length <= 500 ? message : message[..500]);
                command.ExecuteNonQuery();
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

    internal bool TryStartRecording(string? name, out HistoryRecording? recording, out string? error)
    {
        recording = null;
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            if (ReadActiveRecordingId(connection, null) is not null)
            {
                error = "A history recording is already active.";
                return false;
            }
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid().ToString("N");
            var recordingName = string.IsNullOrWhiteSpace(name) ? $"Recording {now:yyyy-MM-dd HH-mm}" : name.Trim();
            if (recordingName.Length > 120) recordingName = recordingName[..120];
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO history_recordings (id, name, started_utc, created_utc)
                VALUES ($id, $name, $started, $created);";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", recordingName);
            command.Parameters.AddWithValue("$started", now.ToString("O"));
            command.Parameters.AddWithValue("$created", now.ToString("O"));
            command.ExecuteNonQuery();
            recording = new HistoryRecording(id, recordingName, now, null, 0, 0);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryStopActiveRecording(out HistoryRecording? recording, out string? error)
    {
        recording = null;
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            var id = ReadActiveRecordingId(connection, null);
            if (id is null)
            {
                error = "No history recording is active.";
                return false;
            }
            var ended = DateTimeOffset.UtcNow;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE history_recordings SET ended_utc = $ended WHERE id = $id;";
                command.Parameters.AddWithValue("$ended", ended.ToString("O"));
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }
            recording = ReadRecording(connection, id);
            return recording is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryGetActiveRecording(out HistoryRecording? recording, out string? error)
    {
        recording = null;
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            var id = ReadActiveRecordingId(connection, null);
            recording = id is null ? null : ReadRecording(connection, id);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListRecordings(out List<HistoryRecording> recordings, out string? error)
    {
        recordings = [];
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM history_recordings ORDER BY started_utc DESC;";
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            reader.Close();
            foreach (var id in ids)
            {
                if (ReadRecording(connection, id) is { } item) recordings.Add(item);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListRecordingSales(string recordingId, out List<SaleHistoryRecord> sales, out string? error)
    {
        sales = [];
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM sales WHERE recording_id = $id ORDER BY completed_utc;";
            command.Parameters.AddWithValue("$id", recordingId);
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            reader.Close();
            foreach (var id in ids)
            {
                if (ReadSale(connection, id) is { } sale) sales.Add(sale);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryArchiveCurrentHistory(out HistoryArchive? archive, out string? error)
    {
        archive = null;
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            if (ReadActiveRecordingId(connection, null) is not null)
            {
                error = "Stop the active recording before resetting history.";
                return false;
            }
            using var transaction = connection.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid().ToString("N");
            var name = $"History {now:yyyy-MM-dd HH-mm}";
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO history_archives (id, name, created_utc) VALUES ($id, $name, $created);";
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$name", name);
                insert.Parameters.AddWithValue("$created", now.ToString("O"));
                insert.ExecuteNonQuery();
            }
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE sales SET archive_id = $id WHERE archive_id IS NULL;";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            archive = ReadArchive(connection, id);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListArchives(out List<HistoryArchive> archives, out string? error)
    {
        archives = [];
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM history_archives ORDER BY created_utc DESC;";
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            reader.Close();
            foreach (var id in ids)
            {
                if (ReadArchive(connection, id) is { } item) archives.Add(item);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryRestoreArchive(string archiveId, out string? error)
    {
        error = null;
        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error)) return false;
            using var transaction = connection.BeginTransaction();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE sales SET archive_id = NULL WHERE archive_id = $id;";
                update.Parameters.AddWithValue("$id", archiveId);
                if (update.ExecuteNonQuery() == 0)
                {
                    error = "History archive is empty or missing.";
                    return false;
                }
            }
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM history_archives WHERE id = $id;";
                delete.Parameters.AddWithValue("$id", archiveId);
                delete.ExecuteNonQuery();
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

    private static string? ReadActiveRecordingId(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM history_recordings WHERE ended_utc IS NULL ORDER BY started_utc DESC LIMIT 1;";
        return command.ExecuteScalar() as string;
    }

    private static SaleHistoryRecord? ReadSale(SqliteConnection connection, string saleId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, completed_utc, event_name, register_name, operator_username,
                   payment_method, is_showcase, subtotal_cents, tip_cents, total_cents,
                   given_cents, change_cents, event_id, event_member_id, event_nickname, recording_id
            FROM sales WHERE id = $id;";
        command.Parameters.AddWithValue("$id", saleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var id = reader.GetString(0);
        var completed = DateTimeOffset.TryParse(reader.GetString(1), out var parsed) ? parsed : DateTimeOffset.MinValue;
        var eventName = reader.GetString(2);
        var registerName = reader.GetString(3);
        var username = reader.GetString(4);
        var payment = reader.GetString(5);
        var showcase = reader.GetInt64(6) != 0;
        var subtotal = reader.GetInt64(7);
        var tip = reader.GetInt64(8);
        var total = reader.GetInt64(9);
        var given = reader.GetInt64(10);
        var change = reader.GetInt64(11);
        var eventId = !reader.IsDBNull(12) && Guid.TryParse(reader.GetString(12), out var parsedEventId) ? parsedEventId : (Guid?)null;
        var memberId = !reader.IsDBNull(13) && Guid.TryParse(reader.GetString(13), out var parsedMemberId) ? parsedMemberId : (Guid?)null;
        var nickname = reader.IsDBNull(14) ? null : reader.GetString(14);
        var recordingId = reader.IsDBNull(15) ? null : reader.GetString(15);
        reader.Close();

        var lines = new List<SaleHistoryLine>();
        using var lineCommand = connection.CreateCommand();
        lineCommand.CommandText = @"
            SELECT item_id, name, unit_cents, quantity, line_total_cents
            FROM sale_lines WHERE sale_id = $id ORDER BY line_index;";
        lineCommand.Parameters.AddWithValue("$id", saleId);
        using var lineReader = lineCommand.ExecuteReader();
        while (lineReader.Read())
        {
            lines.Add(new SaleHistoryLine(
                lineReader.GetString(0),
                lineReader.GetString(1),
                lineReader.GetInt64(2),
                lineReader.GetInt32(3),
                lineReader.GetInt64(4)));
        }
        return new SaleHistoryRecord(id, completed, eventName, registerName, username, payment, showcase, subtotal, tip, total, given, change, lines, eventId, memberId, nickname, recordingId);
    }

    private static HistoryRecording? ReadRecording(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.id, r.name, r.started_utc, r.ended_utc,
                   COUNT(s.id), COALESCE(SUM(s.total_cents), 0)
            FROM history_recordings r
            LEFT JOIN sales s ON s.recording_id = r.id
            WHERE r.id = $id
            GROUP BY r.id, r.name, r.started_utc, r.ended_utc;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var started = DateTimeOffset.Parse(reader.GetString(2));
        DateTimeOffset? ended = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3));
        return new HistoryRecording(reader.GetString(0), reader.GetString(1), started, ended, reader.GetInt64(4), reader.GetInt64(5));
    }

    private static HistoryArchive? ReadArchive(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT a.id, a.name, a.created_utc, COUNT(s.id), COALESCE(SUM(s.total_cents), 0)
            FROM history_archives a
            LEFT JOIN sales s ON s.archive_id = a.id
            WHERE a.id = $id
            GROUP BY a.id, a.name, a.created_utc;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new HistoryArchive(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), reader.GetInt64(3), reader.GetInt64(4));
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
