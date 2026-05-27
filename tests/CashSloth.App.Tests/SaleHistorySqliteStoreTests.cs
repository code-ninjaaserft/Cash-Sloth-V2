using CashSloth.App;
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
