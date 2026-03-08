using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CashSloth.App;

internal sealed class FxRateProvider
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private readonly Dictionary<UiCurrency, decimal> _ratesFromChf = new()
    {
        [UiCurrency.Chf] = 1m,
        [UiCurrency.Eur] = 1.04m,
        [UiCurrency.Usd] = 1.14m,
        [UiCurrency.Gbp] = 0.89m
    };

    internal decimal GetRateFromChf(UiCurrency currency)
    {
        return _ratesFromChf.TryGetValue(currency, out var rate) && rate > 0m
            ? rate
            : 1m;
    }

    internal bool TryRefreshRates(out string? error)
    {
        const string url = "https://api.frankfurter.app/latest?from=CHF&to=EUR,USD,GBP";

        try
        {
            var json = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("rates", out var ratesElement))
            {
                error = "Exchange rates payload has no 'rates'.";
                return false;
            }

            UpdateRateIfPresent(ratesElement, "EUR", UiCurrency.Eur);
            UpdateRateIfPresent(ratesElement, "USD", UiCurrency.Usd);
            UpdateRateIfPresent(ratesElement, "GBP", UiCurrency.Gbp);
            _ratesFromChf[UiCurrency.Chf] = 1m;

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void UpdateRateIfPresent(JsonElement ratesElement, string symbol, UiCurrency currency)
    {
        if (!ratesElement.TryGetProperty(symbol, out var valueElement))
        {
            return;
        }

        if (!decimal.TryParse(valueElement.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate <= 0m)
        {
            return;
        }

        _ratesFromChf[currency] = rate;
    }
}
