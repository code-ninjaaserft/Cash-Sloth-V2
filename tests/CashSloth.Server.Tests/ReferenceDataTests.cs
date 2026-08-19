using System.Net;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Tests;

public sealed class ReferenceDataTests
{
    [Fact]
    public async Task StaleExchangeRates_AreReturnedWhenProviderFails()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        db.ExchangeRateSnapshots.Add(new ExchangeRateSnapshot
        {
            BaseCurrency = "CHF",
            RateDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            FetchedAtUtc = DateTimeOffset.UtcNow.AddHours(-49),
            RatesJson = "{\"EUR\":1.05,\"CHF\":1}"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, scope.ServiceProvider.GetRequiredService<AuditService>());

        var response = await service.GetExchangeRatesAsync(CancellationToken.None);
        Assert.True(response.IsStale);
        Assert.Equal(1.05m, response.Rates["EUR"]);
    }

    [Fact]
    public async Task MissingExchangeRates_Returns503()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var service = CreateService(db, scope.ServiceProvider.GetRequiredService<AuditService>());
        var exception = await Assert.ThrowsAsync<ApiProblemException>(() =>
            service.GetExchangeRatesAsync(CancellationToken.None));
        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task TranslationDictionary_ResolvesOnlyStoredEntries()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var service = CreateService(db, scope.ServiceProvider.GetRequiredService<AuditService>());
        await service.UpsertTranslationAsync(new TranslationUpsertRequest("en", "de", "Coffee", "Kaffee"), "test", CancellationToken.None);

        var response = await service.ResolveTranslationsAsync(
            new TranslationResolveRequest("en", "de", ["Coffee", "Unknown"]),
            CancellationToken.None);
        Assert.True(response.Results[0].Found);
        Assert.Equal("Kaffee", response.Results[0].TranslatedText);
        Assert.False(response.Results[1].Found);
    }

    private static ReferenceDataService CreateService(ServerDbContext db, AuditService audit) =>
        new(db, new StubHttpClientFactory(), new ReferenceDataLock(), audit);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailureHandler())
        {
            BaseAddress = new Uri("https://api.frankfurter.dev/")
        };
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
