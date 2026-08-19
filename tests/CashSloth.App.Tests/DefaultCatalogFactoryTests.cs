using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class DefaultCatalogFactoryTests
{
    [Fact]
    public void ZammeAesseCatalogContainsStand11Offer()
    {
        var catalog = DefaultCatalogFactory.Create("zamme-aesse");

        Assert.Equal(10, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        AssertItem(catalog, "KNUTWILER_MINERAL_CO2", 400);
        AssertItem(catalog, "KNUTWILER_MINERAL_OHNE_CO2", 400);
        AssertItem(catalog, "RAMSEIER_APFELSCHORLE", 800);
        AssertItem(catalog, "KNUTWILER_ICETEA_LEMON", 800);
        AssertItem(catalog, "COCA_COLA", 800);
        AssertItem(catalog, "PEPITA_CITRO", 800);
        AssertItem(catalog, "APPENZELLER_QUOELLFRISCH", 500);
        AssertItem(catalog, "APPENZELLER_PANACHE", 500);
        AssertItem(catalog, "APPENZELLER_QUOELLFRISCH_AF", 500);
        AssertItem(catalog, "EL_TONY_MATE", 500);
    }

    [Fact]
    public void FullProfileKeepsStandardCatalog()
    {
        var catalog = DefaultCatalogFactory.Create("full");

        Assert.Contains(catalog, item => item.Id == "COFFEE");
        Assert.DoesNotContain(catalog, item => item.Id == "EL_TONY_MATE");
    }

    [Fact]
    public void ZammeAesseUsesItsOwnPersistentAssortmentFiles()
    {
        var paths = AssortmentPresetStore.ResolveDefaultPaths("C:\\LocalAppData", "zamme-aesse");

        Assert.EndsWith("assortment.presets.zamme-aesse.json", paths.JsonPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("assortment.presets.zamme-aesse.sqlite3", paths.SqlitePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertItem(IEnumerable<CatalogItemEditor> catalog, string id, long unitCents)
    {
        var item = Assert.Single(catalog, item => item.Id == id);
        Assert.Equal(unitCents, item.UnitCents);
    }
}
