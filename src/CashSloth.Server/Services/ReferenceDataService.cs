using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed class ReferenceDataLock
{
    public SemaphoreSlim ExchangeRates { get; } = new(1, 1);
}

public sealed class ReferenceDataService(
    ServerDbContext db,
    IHttpClientFactory httpClientFactory,
    ReferenceDataLock updateLock,
    AuditService audit)
{
    private static readonly TimeSpan MinimumFetchInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(48);

    public async Task<ExchangeRateResponse> GetExchangeRatesAsync(CancellationToken cancellationToken)
    {
        await updateLock.ExchangeRates.WaitAsync(cancellationToken);
        try
        {
            var latest = await db.ExchangeRateSnapshots.AsNoTracking()
                .Where(value => value.BaseCurrency == "CHF")
                .OrderByDescending(value => value.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is null || DateTimeOffset.UtcNow - latest.FetchedAtUtc >= MinimumFetchInterval)
            {
                latest = await TryFetchAsync(latest, cancellationToken);
            }

            if (latest is null)
            {
                throw new ApiProblemException(503, "exchange_rates_unavailable", "Es ist noch kein Wechselkursstand verfügbar.");
            }

            var rates = JsonSerializer.Deserialize<Dictionary<string, decimal>>(latest.RatesJson)
                ?? new Dictionary<string, decimal>();
            return new ExchangeRateResponse(
                "CHF",
                latest.RateDate,
                latest.FetchedAtUtc,
                DateTimeOffset.UtcNow - latest.FetchedAtUtc >= StaleAfter,
                rates);
        }
        finally
        {
            updateLock.ExchangeRates.Release();
        }
    }

    public async Task<TranslationResolveResponse> ResolveTranslationsAsync(
        TranslationResolveRequest request,
        CancellationToken cancellationToken)
    {
        var source = NormalizeLanguage(request.SourceLanguage);
        var target = NormalizeLanguage(request.TargetLanguage);
        if (request.Texts is not { Length: > 0 } || request.Texts.Length > 100)
        {
            throw new ApiProblemException(400, "invalid_translation_request", "Zwischen 1 und 100 Texte sind erlaubt.");
        }

        var normalizedTexts = request.Texts.Select(NormalizeText).ToArray();
        var entries = await db.TranslationEntries.AsNoTracking()
            .Where(value => value.SourceLanguage == source &&
                            value.TargetLanguage == target &&
                            normalizedTexts.Contains(value.SourceTextNormalized))
            .ToDictionaryAsync(value => value.SourceTextNormalized, StringComparer.Ordinal, cancellationToken);

        var results = request.Texts.Select(text =>
        {
            var normalized = NormalizeText(text);
            return entries.TryGetValue(normalized, out var entry)
                ? new TranslationResolution(text, entry.TranslatedText, true)
                : new TranslationResolution(text, null, false);
        }).ToArray();
        return new TranslationResolveResponse(source, target, results);
    }

    public async Task UpsertTranslationAsync(
        TranslationUpsertRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var source = NormalizeLanguage(request.SourceLanguage);
        var target = NormalizeLanguage(request.TargetLanguage);
        var sourceText = request.SourceText.Trim();
        var translatedText = request.TranslatedText.Trim();
        if (sourceText.Length is < 1 or > 500 || translatedText.Length is < 1 or > 1000)
        {
            throw new ApiProblemException(400, "invalid_translation", "Ausgangstext oder Übersetzung hat eine ungültige Länge.");
        }

        var normalized = NormalizeText(sourceText);
        var entry = await db.TranslationEntries.SingleOrDefaultAsync(value =>
            value.SourceLanguage == source &&
            value.TargetLanguage == target &&
            value.SourceTextNormalized == normalized,
            cancellationToken);
        if (entry is null)
        {
            entry = new TranslationEntry
            {
                SourceLanguage = source,
                TargetLanguage = target,
                SourceText = sourceText,
                SourceTextNormalized = normalized
            };
            db.TranslationEntries.Add(entry);
        }
        entry.TranslatedText = translatedText;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entry.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "translation.upsert", "translation", entry.Id.ToString(CultureInfo.InvariantCulture), $"{source}->{target}", cancellationToken: cancellationToken);
    }

    private async Task<ExchangeRateSnapshot?> TryFetchAsync(
        ExchangeRateSnapshot? fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("frankfurter");
            var rows = await client.GetFromJsonAsync<FrankfurterRate[]>("v2/rates?base=CHF", cancellationToken);
            if (rows is not { Length: > 0 })
            {
                return fallback;
            }

            var date = rows.Max(value => value.Date);
            var rates = rows
                .Where(value => value.Date == date && value.Rate > 0m)
                .GroupBy(value => value.Quote.ToUpperInvariant())
                .ToDictionary(group => group.Key, group => group.Last().Rate, StringComparer.OrdinalIgnoreCase);
            rates["CHF"] = 1m;
            var snapshot = new ExchangeRateSnapshot
            {
                BaseCurrency = "CHF",
                RateDate = date,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                RatesJson = JsonSerializer.Serialize(rates)
            };
            db.ExchangeRateSnapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);

            var obsolete = await db.ExchangeRateSnapshots
                .Where(value => value.BaseCurrency == "CHF" && value.Id != snapshot.Id)
                .OrderByDescending(value => value.Id)
                .Skip(24)
                .ToListAsync(cancellationToken);
            if (obsolete.Count > 0)
            {
                db.ExchangeRateSnapshots.RemoveRange(obsolete);
                await db.SaveChangesAsync(cancellationToken);
            }
            return snapshot;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return fallback;
        }
    }

    private static string NormalizeLanguage(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 12 || !normalized.All(value => char.IsAsciiLetter(value) || value == '-'))
        {
            throw new ApiProblemException(400, "invalid_language", "Sprachcode ist ungültig.");
        }
        return normalized;
    }

    private static string NormalizeText(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed record FrankfurterRate(DateOnly Date, string Base, string Quote, decimal Rate);
}
