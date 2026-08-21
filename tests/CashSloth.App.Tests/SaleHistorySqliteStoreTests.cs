using CashSloth.App;
using CashSloth.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class SaleHistorySqliteStoreTests
{
    [Fact]
    public void RecordsSaleAndListsRecentSales()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new SaleHistorySqliteStore(Path.Combine(tempDir, "sales.sqlite3"));
            Assert.True(store.TryEnsureInitialized(out var initError), initError);

            var sale = BuildSale("event-a", "Kasse 1", "alice", isShowcase: false, subtotalCents: 1200, tipCents: 100);
            Assert.True(store.TryRecordSale(sale, out var saleId, out var recordError), recordError);

            Assert.True(store.TryListRecentSales(10, includeShowcase: false, out var sales, out var listError), listError);
            var listed = Assert.Single(sales);
            Assert.Equal(saleId, listed.Id);
            Assert.Equal("event-a", listed.EventName);
            Assert.Equal("Kasse 1", listed.RegisterName);
            Assert.Equal("alice", listed.OperatorUsername);
            Assert.Equal(1300, listed.TotalCents);
            Assert.Equal(1, listed.LineCount);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void StatisticsExcludeShowcaseSalesByDefault()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new SaleHistorySqliteStore(Path.Combine(tempDir, "sales.sqlite3"));

            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 1", "alice", false, 1000, 100), out _, out var realError), realError);
            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 1", "alice", true, 500, 50), out _, out var showcaseError), showcaseError);

            Assert.True(store.TryGetStatistics(new SaleHistoryFilter(EventName: "event-a"), out var realStats, out var statsError), statsError);
            Assert.Equal(1, realStats.SaleCount);
            Assert.Equal(1000, realStats.SubtotalCents);
            Assert.Equal(100, realStats.TipCents);
            Assert.Equal(1100, realStats.TotalCents);

            Assert.True(store.TryGetStatistics(new SaleHistoryFilter(EventName: "event-a", IncludeShowcase: true), out var allStats, out var allStatsError), allStatsError);
            Assert.Equal(2, allStats.SaleCount);
            Assert.Equal(1500, allStats.SubtotalCents);
            Assert.Equal(150, allStats.TipCents);
            Assert.Equal(1650, allStats.TotalCents);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void StatisticsCanFilterByEventRegisterAndUser()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new SaleHistorySqliteStore(Path.Combine(tempDir, "sales.sqlite3"));

            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 1", "alice", false, 1000, 0), out _, out var firstError), firstError);
            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 2", "alice", false, 2000, 0), out _, out var secondError), secondError);
            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 1", "bob", false, 3000, 0), out _, out var thirdError), thirdError);
            Assert.True(store.TryRecordSale(BuildSale("event-b", "Kasse 1", "alice", false, 4000, 0), out _, out var fourthError), fourthError);

            var filter = new SaleHistoryFilter(
                EventName: "event-a",
                RegisterName: "Kasse 1",
                OperatorUsername: "alice");

            Assert.True(store.TryGetStatistics(filter, out var stats, out var statsError), statsError);
            Assert.Equal(1, stats.SaleCount);
            Assert.Equal(1000, stats.TotalCents);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void EventSale_IsQueuedAtomically_AndRemovedAfterAcknowledgement()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new SaleHistorySqliteStore(Path.Combine(tempDir, "sales.sqlite3"));
            var eventId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            var sale = BuildSale("server-event", "Kasse 2", "alice", false, 450, 50) with
            {
                ServerEventId = eventId,
                EventMemberId = memberId,
                EventNickname = "Kasse 2"
            };

            Assert.True(store.TryRecordSale(sale, out var saleId, out var recordError), recordError);
            Assert.True(store.TryGetPendingEventSaleCount(eventId, out var pending, out var countError), countError);
            Assert.Equal(1, pending);
            Assert.True(store.TryListPendingEventSales(eventId, 10, out var queued, out var listError), listError);
            var queuedSale = Assert.Single(queued);
            Assert.Equal(memberId, queuedSale.MemberId);
            Assert.Equal(saleId, queuedSale.Sale.Id);

            Assert.True(store.TryApplyEventSaleSyncResults([
                new EventSaleUploadResult(saleId, EventSaleSyncDisposition.Accepted, null, null, DateTimeOffset.UtcNow)
            ], out var applyError), applyError);
            Assert.True(store.TryGetPendingEventSaleCount(eventId, out pending, out countError), countError);
            Assert.Equal(0, pending);

            Assert.True(store.TryRecordSale(sale with { Id = "rejected-sale" }, out var rejectedId, out recordError), recordError);
            Assert.True(store.TryApplyEventSaleSyncResults([
                new EventSaleUploadResult(rejectedId, EventSaleSyncDisposition.Rejected, "sale_line_mismatch", "Rejected", null)
            ], out applyError), applyError);
            Assert.True(store.TryGetPendingEventSaleCount(eventId, out pending, out countError), countError);
            Assert.Equal(1, pending);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void RecordingAndRecoverableHistoryReset_RoundTripSales()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new SaleHistorySqliteStore(Path.Combine(tempDir, "sales.sqlite3"));
            Assert.True(store.TryStartRecording("Lunch", out var started, out var startError), startError);
            Assert.NotNull(started);
            Assert.True(store.TryRecordSale(BuildSale("event-a", "Kasse 1", "alice", false, 1000, 0), out _, out var recordError), recordError);
            Assert.True(store.TryStopActiveRecording(out var stopped, out var stopError), stopError);
            Assert.Equal(1, stopped?.SaleCount);
            Assert.True(store.TryListRecordingSales(started!.Id, out var recordedSales, out var recordedError), recordedError);
            Assert.Single(recordedSales);

            Assert.True(store.TryArchiveCurrentHistory(out var archive, out var archiveError), archiveError);
            Assert.NotNull(archive);
            Assert.True(store.TryListRecentSales(10, false, out var hiddenSales, out var hiddenError), hiddenError);
            Assert.Empty(hiddenSales);
            Assert.True(store.TryRestoreArchive(archive!.Id, out var restoreError), restoreError);
            Assert.True(store.TryListRecentSales(10, false, out var restoredSales, out var restoredError), restoredError);
            Assert.Single(restoredSales);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void VersionOneDatabase_MigratesWithoutLosingExistingSales()
    {
        var tempDir = CreateTempDir();
        try
        {
            var path = Path.Combine(tempDir, "sales.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA foreign_keys = ON;
                    CREATE TABLE sales (
                        id TEXT PRIMARY KEY, completed_utc TEXT NOT NULL, event_name TEXT NOT NULL,
                        register_name TEXT NOT NULL, operator_username TEXT NOT NULL, payment_method TEXT NOT NULL,
                        is_showcase INTEGER NOT NULL, subtotal_cents INTEGER NOT NULL, tip_cents INTEGER NOT NULL,
                        total_cents INTEGER NOT NULL, given_cents INTEGER NOT NULL, change_cents INTEGER NOT NULL,
                        line_count INTEGER NOT NULL, created_utc TEXT NOT NULL);
                    CREATE TABLE sale_lines (
                        sale_id TEXT NOT NULL, line_index INTEGER NOT NULL, item_id TEXT NOT NULL, name TEXT NOT NULL,
                        unit_cents INTEGER NOT NULL, quantity INTEGER NOT NULL, line_total_cents INTEGER NOT NULL,
                        PRIMARY KEY (sale_id, line_index), FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE);
                    INSERT INTO sales VALUES ('old-sale', '2026-08-20T10:00:00.0000000+00:00', 'Old event', 'Kasse 1', 'alice', 'Cash', 0, 500, 0, 500, 500, 0, 1, '2026-08-20T10:00:00.0000000+00:00');
                    INSERT INTO sale_lines VALUES ('old-sale', 0, 'COFFEE', 'Coffee', 500, 1, 500);
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            var store = new SaleHistorySqliteStore(path);
            Assert.True(store.TryEnsureInitialized(out var migrateError), migrateError);
            Assert.True(store.TryListRecentSales(10, false, out var sales, out var listError), listError);
            Assert.Equal("old-sale", Assert.Single(sales).Id);
            Assert.True(store.TryStartRecording("After migration", out _, out var recordingError), recordingError);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static SaleHistoryRecord BuildSale(
        string eventName,
        string registerName,
        string username,
        bool isShowcase,
        long subtotalCents,
        long tipCents)
    {
        return new SaleHistoryRecord(
            string.Empty,
            DateTimeOffset.UtcNow,
            eventName,
            registerName,
            username,
            "Cash",
            isShowcase,
            subtotalCents,
            tipCents,
            subtotalCents + tipCents,
            subtotalCents + tipCents,
            0,
            new[]
            {
                new SaleHistoryLine("COFFEE", "Coffee", subtotalCents, 1, subtotalCents)
            });
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "CashSlothSaleHistoryTests", Guid.NewGuid().ToString("N"));
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
