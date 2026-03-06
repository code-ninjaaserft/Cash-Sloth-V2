using System.Globalization;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal static class CurrencyFormatter
{
    internal static string FormatCents(long cents)
    {
        var value = cents / 100.0;
        return string.Format(CultureInfo.InvariantCulture, "CHF {0:0.00}", value);
    }
}

internal sealed record CartSnapshot(
    [property: JsonPropertyName("lines")] CartLine[]? Lines,
    [property: JsonPropertyName("total_cents")] long TotalCents,
    [property: JsonPropertyName("given_cents")] long GivenCents,
    [property: JsonPropertyName("change_cents")] long ChangeCents);

internal sealed record CartLine(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("unit_cents")] long UnitCents,
    [property: JsonPropertyName("qty")] int Qty,
    [property: JsonPropertyName("line_total_cents")] long LineTotalCents);

internal sealed record CartLineView(string Name, int Quantity, string LineTotalDisplay);

internal sealed class CatalogItemEditor
{
    internal CatalogItemEditor(string id, string name, long unitCents, string category)
    {
        Id = id;
        Name = name;
        UnitCents = unitCents;
        Category = category;
    }

    internal string Id { get; set; }
    internal string Name { get; set; }
    internal long UnitCents { get; set; }
    internal string Category { get; set; }
}

internal sealed record CatalogCorePayload(
    [property: JsonPropertyName("items")] CatalogCoreItem[] Items);

internal sealed record CatalogCoreItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit_cents")] long UnitCents);
