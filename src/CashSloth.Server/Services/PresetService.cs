using System.Text.RegularExpressions;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed partial class PresetService(ServerDbContext db, AuditService audit)
{
    private const string ActivePresetMetadataKey = "active-preset-id";

    public async Task<IReadOnlyList<PresetSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var activeId = await GetActiveIdAsync(cancellationToken);
        return await db.Presets.AsNoTracking()
            .OrderBy(value => value.Name)
            .Select(value => new PresetSummaryResponse(
                value.Id,
                value.Name,
                value.Version,
                value.Id == activeId,
                value.Items.Count,
                value.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<PresetDocument> GetAsync(string id, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeId(id);
        var preset = await db.Presets.AsNoTracking()
            .Include(value => value.Categories)
            .Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.Id == normalizedId, cancellationToken)
            ?? throw new ApiProblemException(404, "preset_not_found", $"Preset '{normalizedId}' wurde nicht gefunden.");
        return ToDocument(preset, string.Equals(await GetActiveIdAsync(cancellationToken), preset.Id, StringComparison.Ordinal));
    }

    public async Task<PresetDocument> GetActiveAsync(CancellationToken cancellationToken)
    {
        var activeId = await GetActiveIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(activeId))
        {
            throw new ApiProblemException(404, "active_preset_not_found", "Es ist kein aktives Preset gesetzt.");
        }
        return await GetAsync(activeId, cancellationToken);
    }

    public async Task<PresetWriteResponse> CreateAsync(
        PresetDocument document,
        string actor,
        CancellationToken cancellationToken)
    {
        var validated = Validate(document);
        if (await db.Presets.AnyAsync(value => value.Id == validated.Id, cancellationToken))
        {
            throw new ApiProblemException(409, "preset_exists", $"Preset '{validated.Id}' existiert bereits.");
        }

        var now = DateTimeOffset.UtcNow;
        var preset = new Preset
        {
            Id = validated.Id,
            Name = validated.Name,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        AddChildren(preset, validated);
        db.Presets.Add(preset);

        var activeId = await GetActiveIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(activeId))
        {
            db.ServerMetadata.Add(new ServerMetadata { Key = ActivePresetMetadataKey, Value = preset.Id });
            activeId = preset.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "preset.create", "preset", preset.Id, cancellationToken: cancellationToken);
        return new PresetWriteResponse(preset.Id, preset.Version, activeId == preset.Id);
    }

    public async Task<PresetWriteResponse> UpdateAsync(
        string id,
        PresetDocument document,
        string actor,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeId(id);
        var validated = Validate(document with { Id = normalizedId });
        if (validated.Version <= 0)
        {
            throw new ApiProblemException(400, "preset_version_required", "Für Änderungen ist die geladene Preset-Version erforderlich.");
        }

        var preset = await db.Presets
            .Include(value => value.Categories)
            .Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.Id == normalizedId, cancellationToken)
            ?? throw new ApiProblemException(404, "preset_not_found", $"Preset '{normalizedId}' wurde nicht gefunden.");

        if (preset.Version != validated.Version)
        {
            throw new ApiProblemException(409, "preset_version_conflict", "Das Preset wurde zwischenzeitlich geändert. Bitte neu laden.");
        }

        db.Entry(preset).Property(value => value.Version).OriginalValue = validated.Version;
        db.PresetCategories.RemoveRange(preset.Categories);
        db.PresetItems.RemoveRange(preset.Items);
        preset.Categories.Clear();
        preset.Items.Clear();
        preset.Name = validated.Name;
        preset.Version++;
        preset.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddChildren(preset, validated);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiProblemException(409, "preset_version_conflict", "Das Preset wurde zwischenzeitlich geändert. Bitte neu laden.");
        }

        await audit.WriteAsync(actor, "preset.update", "preset", preset.Id, $"Version {preset.Version}", cancellationToken: cancellationToken);
        return new PresetWriteResponse(preset.Id, preset.Version, await GetActiveIdAsync(cancellationToken) == preset.Id);
    }

    public async Task SetActiveAsync(string id, string actor, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeId(id);
        if (!await db.Presets.AnyAsync(value => value.Id == normalizedId, cancellationToken))
        {
            throw new ApiProblemException(404, "preset_not_found", $"Preset '{normalizedId}' wurde nicht gefunden.");
        }

        var metadata = await db.ServerMetadata.SingleOrDefaultAsync(value => value.Key == ActivePresetMetadataKey, cancellationToken);
        if (metadata is null)
        {
            db.ServerMetadata.Add(new ServerMetadata { Key = ActivePresetMetadataKey, Value = normalizedId });
        }
        else
        {
            metadata.Value = normalizedId;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "preset.activate", "preset", normalizedId, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string id, string actor, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeId(id);
        var preset = await db.Presets.SingleOrDefaultAsync(value => value.Id == normalizedId, cancellationToken)
            ?? throw new ApiProblemException(404, "preset_not_found", $"Preset '{normalizedId}' wurde nicht gefunden.");
        var activeMetadata = await db.ServerMetadata.SingleOrDefaultAsync(value => value.Key == ActivePresetMetadataKey, cancellationToken);
        db.Presets.Remove(preset);
        if (activeMetadata?.Value == normalizedId)
        {
            var fallback = await db.Presets.AsNoTracking()
                .Where(value => value.Id != normalizedId)
                .OrderBy(value => value.Name)
                .Select(value => value.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (fallback is null)
            {
                db.ServerMetadata.Remove(activeMetadata);
            }
            else
            {
                activeMetadata.Value = fallback;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "preset.delete", "preset", normalizedId, cancellationToken: cancellationToken);
    }

    public static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return InvalidPresetIdCharacters().Replace(value.Trim().ToUpperInvariant(), "_").Trim('_');
    }

    private async Task<string?> GetActiveIdAsync(CancellationToken cancellationToken) =>
        await db.ServerMetadata.AsNoTracking()
            .Where(value => value.Key == ActivePresetMetadataKey)
            .Select(value => value.Value)
            .SingleOrDefaultAsync(cancellationToken);

    private static PresetDocument Validate(PresetDocument document)
    {
        var id = NormalizeId(document.Id);
        if (id.Length is < 1 or > 80)
        {
            throw new ApiProblemException(400, "invalid_preset_id", "Preset-ID muss 1 bis 80 Zeichen lang sein.");
        }
        var name = document.Name.Trim();
        if (name.Length is < 1 or > 200)
        {
            throw new ApiProblemException(400, "invalid_preset_name", "Presetname muss 1 bis 200 Zeichen lang sein.");
        }
        if (document.Items is not { Length: > 0 })
        {
            throw new ApiProblemException(400, "preset_items_required", "Preset muss mindestens einen Artikel enthalten.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var categories = new HashSet<string>(
            (document.Categories ?? []).Select(value => value.Trim()).Where(value => value.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var items = new List<PresetItemDocument>();
        foreach (var item in document.Items)
        {
            var itemId = NormalizeId(item.Id);
            if (itemId.Length is < 1 or > 80 || !ids.Add(itemId))
            {
                throw new ApiProblemException(400, "invalid_preset_item", "Artikel-IDs müssen eindeutig und 1 bis 80 Zeichen lang sein.");
            }
            if (item.UnitCents < 0)
            {
                throw new ApiProblemException(400, "invalid_preset_price", $"Artikel '{itemId}' hat einen negativen Preis.");
            }
            var itemName = item.Name.Trim();
            var category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category.Trim();
            if (itemName.Length is < 1 or > 200 || category.Length > 100)
            {
                throw new ApiProblemException(400, "invalid_preset_item", $"Artikel '{itemId}' enthält ungültige Texte.");
            }
            categories.Add(category);
            items.Add(new PresetItemDocument(itemId, itemName, item.UnitCents, category));
        }

        return document with
        {
            Id = id,
            Name = name,
            Categories = categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Items = items.ToArray()
        };
    }

    private static void AddChildren(Preset preset, PresetDocument document)
    {
        var categoryIndex = 0;
        foreach (var category in document.Categories)
        {
            preset.Categories.Add(new PresetCategory { PresetId = preset.Id, Name = category, SortOrder = categoryIndex++ });
        }
        var itemIndex = 0;
        foreach (var item in document.Items)
        {
            preset.Items.Add(new PresetItem
            {
                PresetId = preset.Id,
                Id = item.Id,
                Name = item.Name,
                UnitCents = item.UnitCents,
                Category = item.Category,
                SortOrder = itemIndex++
            });
        }
    }

    private static PresetDocument ToDocument(Preset preset, bool isActive) => new(
        preset.Id,
        preset.Name,
        preset.Categories.OrderBy(value => value.SortOrder).Select(value => value.Name).ToArray(),
        preset.Items.OrderBy(value => value.SortOrder)
            .Select(value => new PresetItemDocument(value.Id, value.Name, value.UnitCents, value.Category))
            .ToArray(),
        preset.Version,
        isActive,
        preset.UpdatedAtUtc);

    [GeneratedRegex("[^A-Z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidPresetIdCharacters();
}
