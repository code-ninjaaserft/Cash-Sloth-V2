namespace CashSloth.App;

internal static class DefaultCatalogFactory
{
    internal const string ZammeAesseProfile = "zamme-aesse";

    internal static List<CatalogItemEditor> Create(string? profile)
    {
        return string.Equals(profile?.Trim(), ZammeAesseProfile, StringComparison.OrdinalIgnoreCase)
            ? CreateZammeAesseCatalog()
            : CreateFullCatalog();
    }

    private static List<CatalogItemEditor> CreateZammeAesseCatalog()
    {
        return
        [
            new("KNUTWILER_MINERAL_CO2", "Knutwiler Mineral mit CO2 (5 dl Flasche)", 400, "Alkoholfrei"),
            new("KNUTWILER_MINERAL_OHNE_CO2", "Knutwiler Mineral ohne CO2 (5 dl Flasche)", 400, "Alkoholfrei"),
            new("RAMSEIER_APFELSCHORLE", "Ramseier Apfelschorle (1.5 l Flasche)", 800, "Alkoholfrei"),
            new("KNUTWILER_ICETEA_LEMON", "Knutwiler IceTea lemon (1.5 l Flasche)", 800, "Alkoholfrei"),
            new("COCA_COLA", "Coca Cola (1.5 l Flasche)", 800, "Alkoholfrei"),
            new("PEPITA_CITRO", "Pepita Citro (1.5 l Flasche)", 800, "Alkoholfrei"),
            new("APPENZELLER_QUOELLFRISCH", "Appenzeller Quöllfrisch (4 dl Becher)", 500, "Bier & Panaché"),
            new("APPENZELLER_PANACHE", "Appenzeller Panaché (4 dl Becher)", 500, "Bier & Panaché"),
            new("APPENZELLER_QUOELLFRISCH_AF", "Appenzeller Quöllfrisch alkoholfrei (5 dl Dose)", 500, "Alkoholfrei"),
            new("EL_TONY_MATE", "El Tony Mate (3 dl Dose)", 500, "Alkoholfrei")
        ];
    }

    private static List<CatalogItemEditor> CreateFullCatalog()
    {
        return
        [
            new("COFFEE", "Coffee", 500, "Hot Drinks"),
            new("TEA", "Tea", 400, "Hot Drinks"),
            new("WATER", "Water", 200, "Soft Drinks"),
            new("COLA", "Cola", 350, "Soft Drinks"),
            new("CHIPS", "Chips", 250, "Snacks"),
            new("CAKE", "Cake", 450, "Snacks")
        ];
    }
}
