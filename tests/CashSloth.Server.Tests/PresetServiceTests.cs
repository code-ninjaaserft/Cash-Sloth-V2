using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Services;

namespace CashSloth.Server.Tests;

public sealed class PresetServiceTests
{
    [Fact]
    public async Task Preset_CreateActivateAndUpdate_UsesOptimisticVersion()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var presets = scope.ServiceProvider.GetRequiredService<PresetService>();
        var original = CreatePreset("SUMMER", "Summer");
        var created = await presets.CreateAsync(original, "test", CancellationToken.None);
        Assert.Equal(1, created.Version);
        Assert.True(created.IsActive);

        var loaded = await presets.GetActiveAsync(CancellationToken.None);
        var updated = await presets.UpdateAsync("SUMMER", loaded with { Name = "Summer 2" }, "test", CancellationToken.None);
        Assert.Equal(2, updated.Version);

        var stale = await Assert.ThrowsAsync<ApiProblemException>(() =>
            presets.UpdateAsync("SUMMER", loaded with { Name = "Stale" }, "test", CancellationToken.None));
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal("preset_version_conflict", stale.Code);
    }

    [Fact]
    public async Task Preset_RejectsDuplicateItemsAndNegativePrices()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var presets = scope.ServiceProvider.GetRequiredService<PresetService>();
        var invalid = new PresetDocument(
            "INVALID",
            "Invalid",
            ["Drinks"],
            [
                new PresetItemDocument("A", "A", 100, "Drinks"),
                new PresetItemDocument("A", "B", -1, "Drinks")
            ]);

        var exception = await Assert.ThrowsAsync<ApiProblemException>(() =>
            presets.CreateAsync(invalid, "test", CancellationToken.None));
        Assert.Equal(400, exception.StatusCode);
    }

    internal static PresetDocument CreatePreset(string id, string name) => new(
        id,
        name,
        ["Drinks"],
        [new PresetItemDocument("COFFEE", "Coffee", 450, "Drinks")]);
}
