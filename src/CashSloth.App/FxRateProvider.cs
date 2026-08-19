namespace CashSloth.App;

internal sealed class FxRateProvider
{
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
        error = "Exchange rates are refreshed through the configured CashSloth server.";
        return false;
    }

    internal void UpdateFromServer(IReadOnlyDictionary<string, decimal> rates)
    {
        UpdateRateIfPresent(rates, "EUR", UiCurrency.Eur);
        UpdateRateIfPresent(rates, "USD", UiCurrency.Usd);
        UpdateRateIfPresent(rates, "GBP", UiCurrency.Gbp);
        _ratesFromChf[UiCurrency.Chf] = 1m;
    }

    private void UpdateRateIfPresent(IReadOnlyDictionary<string, decimal> rates, string symbol, UiCurrency currency)
    {
        if (!rates.TryGetValue(symbol, out var rate) || rate <= 0m)
        {
            return;
        }
        _ratesFromChf[currency] = rate;
    }
}
