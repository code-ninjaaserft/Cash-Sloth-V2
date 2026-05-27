using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class OnlinePresetProviderTests
{
    [Fact]
    public void UploadRejectsInvalidUrl()
    {
        var provider = new OnlinePresetProvider();
        var preset = new AssortmentPresetDocument(
            "TEST",
            "Test",
            new[] { "General" },
            new[] { new AssortmentPresetItemDocument("ITEM_1", "Item 1", 100, "General") });

        var ok = provider.TryUploadPreset("not-a-valid-url", preset, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("valid HTTP or HTTPS URL", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UploadRejectsPresetWithoutItems()
    {
        var provider = new OnlinePresetProvider();
        var preset = new AssortmentPresetDocument(
            "TEST",
            "Test",
            new[] { "General" },
            Array.Empty<AssortmentPresetItemDocument>());

        var ok = provider.TryUploadPreset("https://example.org/api/presets/upload", preset, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("does not contain valid items", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadSummariesRejectsInvalidServerUrl()
    {
        var provider = new OnlinePresetProvider();

        var ok = provider.TryDownloadPresetSummaries("not-a-valid-url", out var summaries, out var error);

        Assert.False(ok);
        Assert.Empty(summaries);
        Assert.NotNull(error);
        Assert.Contains("valid HTTP or HTTPS URL", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadPresetByIdRejectsMissingPresetId()
    {
        var provider = new OnlinePresetProvider();

        var ok = provider.TryDownloadPresetById("https://example.org", "   ", out var preset, out var error);

        Assert.False(ok);
        Assert.Null(preset);
        Assert.NotNull(error);
        Assert.Contains("must not be empty", error, StringComparison.OrdinalIgnoreCase);
    }
}
