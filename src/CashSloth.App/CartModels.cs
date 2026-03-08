using System.Globalization;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal static class CurrencyFormatter
{
    private static UiCurrency _currency = UiCurrency.Chf;
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en-GB");
    private static decimal _rateFromChf = 1m;

    private static readonly string[] KnownCurrencyTokens =
    {
        "CHF",
        "EUR",
        "USD",
        "GBP",
        "FR.",
        "FR",
        "SFR",
        "€",
        "$",
        "£"
    };

    internal static UiCurrency CurrentCurrency => _currency;

    internal static void Configure(UiCurrency currency, CultureInfo culture, decimal rateFromChf)
    {
        _currency = currency;
        _culture = culture;
        _rateFromChf = rateFromChf <= 0m ? 1m : rateFromChf;
    }

    internal static string FormatCents(long cents)
    {
        var displayCents = ConvertBaseCentsToDisplayCents(cents);
        var value = displayCents / 100m;
        var number = value.ToString("0.00", _culture);
        return $"{GetCurrencyCode(_currency)} {number}";
    }

    internal static long ConvertBaseCentsToDisplayCents(long baseCents)
    {
        return (long)decimal.Round(baseCents * _rateFromChf, 0, MidpointRounding.AwayFromZero);
    }

    internal static long ConvertDisplayCentsToBaseCents(long displayCents)
    {
        if (_rateFromChf <= 0m)
        {
            return displayCents;
        }

        return (long)decimal.Round(displayCents / _rateFromChf, 0, MidpointRounding.AwayFromZero);
    }

    internal static string FormatQuickTenderLabel(long cents)
    {
        var value = cents / 100m;
        if (value < 1m)
        {
            return value.ToString("0.00", _culture);
        }

        var whole = decimal.Truncate(value);
        return value == whole
            ? whole.ToString("0", _culture)
            : value.ToString("0.00", _culture);
    }

    internal static string StripKnownCurrencyTokens(string input)
    {
        var cleaned = input;
        foreach (var token in KnownCurrencyTokens)
        {
            cleaned = cleaned.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return cleaned.Trim();
    }

    internal static string GetCurrencyCode(UiCurrency currency)
    {
        return currency switch
        {
            UiCurrency.Eur => "EUR",
            UiCurrency.Usd => "USD",
            UiCurrency.Gbp => "GBP",
            _ => "CHF"
        };
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
