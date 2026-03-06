using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal sealed class AssortmentPresetStore
{
    private const int CurrentSchemaVersion = 1;
    private const string DefaultPresetId = "default";
    private const string DefaultPresetName = "Default Sortiment";
    private const string DefaultCategory = "General";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal AssortmentPresetStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = Path.Combine(localAppData, "CashSloth", "assortment.presets.json");
    }

    internal string FilePath { get; }

    internal bool TryLoad(out List<CatalogItemEditor> catalog, out List<string> extraCategories, out string? error)
    {
        catalog = new List<CatalogItemEditor>();
        extraCategories = new List<string>();

        if (!File.Exists(FilePath))
        {
            error = null;
            return false;
        }

        if (!TryReadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        if (document.SchemaVersion > CurrentSchemaVersion)
        {
            error = $"Unsupported assortment schema version {document.SchemaVersion}.";
            return false;
        }

        if (document.Presets == null || document.Presets.Length == 0)
        {
            error = "Assortment JSON must include at least one preset.";
            return false;
        }

        var activePresetId = string.IsNullOrWhiteSpace(document.ActivePresetId)
            ? DefaultPresetId
            : document.ActivePresetId.Trim();

        var activePreset = document.Presets.FirstOrDefault(preset =>
            string.Equals(preset.Id, activePresetId, StringComparison.OrdinalIgnoreCase))
            ?? document.Presets[0];

        if (activePreset.Items == null || activePreset.Items.Length == 0)
        {
            error = $"Preset '{activePreset.Id}' has no items.";
            return false;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in activePreset.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                error = "Preset item id must not be empty.";
                return false;
            }

            if (!seenIds.Add(item.Id))
            {
                error = $"Preset contains duplicate item id '{item.Id}'.";
                return false;
            }

            if (item.UnitCents < 0)
            {
                error = $"Preset item '{item.Id}' has a negative unit_cents value.";
                return false;
            }

            var category = string.IsNullOrWhiteSpace(item.Category) ? DefaultCategory : item.Category.Trim();
            var name = string.IsNullOrWhiteSpace(item.Name) ? item.Id.Trim() : item.Name.Trim();

            catalog.Add(new CatalogItemEditor(item.Id.Trim(), name, item.UnitCents, category));
        }

        if (catalog.Count == 0)
        {
            error = $"Preset '{activePreset.Id}' has no valid items.";
            return false;
        }

        var knownItemCategories = new HashSet<string>(
            catalog.Select(item => item.Category),
            StringComparer.OrdinalIgnoreCase);

        if (activePreset.Categories != null)
        {
            foreach (var category in activePreset.Categories)
            {
                var trimmed = category?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || knownItemCategories.Contains(trimmed))
                {
                    continue;
                }

                knownItemCategories.Add(trimmed);
                extraCategories.Add(trimmed);
            }
        }

        error = null;
        return true;
    }

    internal bool TrySave(IReadOnlyCollection<CatalogItemEditor> catalog, IReadOnlyCollection<string> extraCategories, out string? error)
    {
        if (catalog.Count == 0)
        {
            error = "Catalog cannot be persisted without at least one item.";
            return false;
        }

        var allCategories = catalog
            .Select(item => item.Category.Trim())
            .Concat(extraCategories.Select(category => category.Trim()))
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var savedItems = catalog
            .Select(item => new AssortmentPresetItemDocument(
                item.Id.Trim(),
                item.Name.Trim(),
                item.UnitCents,
                string.IsNullOrWhiteSpace(item.Category) ? DefaultCategory : item.Category.Trim()))
            .ToArray();

        var activePresetId = DefaultPresetId;
        var activePresetName = DefaultPresetName;
        var remainingPresets = new List<AssortmentPresetDocument>();

        if (TryReadDocument(out var existingDocument, out _) && existingDocument?.Presets is { Length: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(existingDocument.ActivePresetId))
            {
                activePresetId = existingDocument.ActivePresetId.Trim();
            }

            foreach (var preset in existingDocument.Presets)
            {
                if (string.IsNullOrWhiteSpace(preset.Id))
                {
                    continue;
                }

                if (string.Equals(preset.Id, activePresetId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(preset.Name))
                    {
                        activePresetName = preset.Name.Trim();
                    }

                    continue;
                }

                remainingPresets.Add(preset);
            }
        }

        var activePreset = new AssortmentPresetDocument(
            activePresetId,
            activePresetName,
            allCategories,
            savedItems);

        var document = new AssortmentStoreDocument(
            CurrentSchemaVersion,
            activePresetId,
            new[] { activePreset }.Concat(remainingPresets).ToArray());

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(document, _jsonOptions);
            File.WriteAllText(FilePath, json);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryReadDocument(out AssortmentStoreDocument? document, out string? error)
    {
        document = null;
        try
        {
            var json = File.ReadAllText(FilePath);
            document = JsonSerializer.Deserialize<AssortmentStoreDocument>(json, _jsonOptions);
            if (document == null)
            {
                error = "Assortment JSON is empty or invalid.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

internal sealed record AssortmentStoreDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("active_preset_id")] string ActivePresetId,
    [property: JsonPropertyName("presets")] AssortmentPresetDocument[] Presets);

internal sealed record AssortmentPresetDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("categories")] string[] Categories,
    [property: JsonPropertyName("items")] AssortmentPresetItemDocument[] Items);

internal sealed record AssortmentPresetItemDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit_cents")] long UnitCents,
    [property: JsonPropertyName("category")] string Category);

