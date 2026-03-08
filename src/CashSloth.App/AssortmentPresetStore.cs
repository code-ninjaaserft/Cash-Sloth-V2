using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CashSloth.App;

internal sealed class AssortmentPresetStore
{
    private const int CurrentSchemaVersion = 1;
    private const string DefaultPresetId = "default";
    private const string DefaultPresetName = "Default Sortiment";
    private const string DefaultCategory = "General";
    private static readonly Regex PresetIdRegex = new("[^A-Z0-9]+", RegexOptions.Compiled);

    private AssortmentSqliteStore _sqliteStore = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal AssortmentPresetStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var jsonPath = Path.Combine(localAppData, "CashSloth", "assortment.presets.json");
        var sqlitePath = Path.Combine(localAppData, "CashSloth", "assortment.presets.sqlite3");
        Initialize(jsonPath, sqlitePath);
    }

    internal AssortmentPresetStore(string jsonFilePath, string sqliteFilePath)
    {
        Initialize(jsonFilePath, sqliteFilePath);
    }

    internal string FilePath { get; private set; } = string.Empty;
    internal string SqliteFilePath { get; private set; } = string.Empty;

    private void Initialize(string jsonFilePath, string sqliteFilePath)
    {
        FilePath = jsonFilePath;
        SqliteFilePath = sqliteFilePath;
        _sqliteStore = new AssortmentSqliteStore(SqliteFilePath);
    }

    internal bool TryLoad(out List<CatalogItemEditor> catalog, out List<string> extraCategories, out string? error)
    {
        catalog = new List<CatalogItemEditor>();
        extraCategories = new List<string>();

        if (!TryLoadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        return TryBuildCatalogFromDocument(document, null, out catalog, out extraCategories, out error);
    }

    internal bool TryLoadPreset(string presetId, out List<CatalogItemEditor> catalog, out List<string> extraCategories, out string? error)
    {
        catalog = new List<CatalogItemEditor>();
        extraCategories = new List<string>();

        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return false;
        }

        if (!TryLoadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        return TryBuildCatalogFromDocument(document, normalizedPresetId, out catalog, out extraCategories, out error);
    }

    internal bool TryGetPresetSummaries(out List<AssortmentPresetSummary> summaries, out string? error)
    {
        summaries = new List<AssortmentPresetSummary>();

        if (!TryLoadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        if (document.Presets == null || document.Presets.Length == 0)
        {
            error = "Assortment JSON must include at least one preset.";
            return false;
        }

        var activePresetId = ResolveActivePresetId(document);

        foreach (var preset in document.Presets
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Id))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(preset => preset.Id, StringComparer.OrdinalIgnoreCase))
        {
            summaries.Add(new AssortmentPresetSummary(
                NormalizePresetId(preset.Id),
                string.IsNullOrWhiteSpace(preset.Name) ? NormalizePresetId(preset.Id) : preset.Name.Trim(),
                string.Equals(NormalizePresetId(preset.Id), activePresetId, StringComparison.OrdinalIgnoreCase),
                preset.Items?.Length ?? 0));
        }

        error = null;
        return true;
    }

    internal bool TrySetActivePreset(string presetId, out string? error)
    {
        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return false;
        }

        if (!TryLoadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        if (!document.Presets.Any(preset => string.Equals(NormalizePresetId(preset.Id), normalizedPresetId, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Preset '{normalizedPresetId}' does not exist.";
            return false;
        }

        var updated = document with { ActivePresetId = normalizedPresetId };
        return TrySaveDocument(updated, out error);
    }

    internal bool TrySave(IReadOnlyCollection<CatalogItemEditor> catalog, IReadOnlyCollection<string> extraCategories, out string? error)
    {
        if (catalog.Count == 0)
        {
            error = "Catalog cannot be persisted without at least one item.";
            return false;
        }

        if (!TryValidateCatalog(catalog, out error))
        {
            return false;
        }

        if (!TryLoadOrInitializeDocument(out var document, out error))
        {
            return false;
        }

        var activePresetId = ResolveActivePresetId(document);
        var activePresetName = document.Presets
            .FirstOrDefault(preset => string.Equals(NormalizePresetId(preset.Id), activePresetId, StringComparison.OrdinalIgnoreCase))
            ?.Name;

        if (string.IsNullOrWhiteSpace(activePresetName))
        {
            activePresetName = DefaultPresetName;
        }

        return TryUpsertPreset(activePresetId, activePresetName, catalog, extraCategories, true, out error);
    }

    internal bool TryUpsertPreset(
        string presetId,
        string presetName,
        IReadOnlyCollection<CatalogItemEditor> catalog,
        IReadOnlyCollection<string> extraCategories,
        bool setActive,
        out string? error)
    {
        if (catalog.Count == 0)
        {
            error = "Catalog cannot be persisted without at least one item.";
            return false;
        }

        if (!TryValidateCatalog(catalog, out error))
        {
            return false;
        }

        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return false;
        }

        var normalizedPresetName = string.IsNullOrWhiteSpace(presetName)
            ? normalizedPresetId
            : presetName.Trim();

        if (!TryLoadOrInitializeDocument(out var document, out error))
        {
            return false;
        }

        var preset = BuildPresetDocument(normalizedPresetId, normalizedPresetName, catalog, extraCategories);

        var mergedPresets = document.Presets
            .Where(existing => !string.Equals(NormalizePresetId(existing.Id), normalizedPresetId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        mergedPresets.Add(preset);

        var activePresetId = setActive
            ? normalizedPresetId
            : ResolveActivePresetId(new AssortmentStoreDocument(document.SchemaVersion, document.ActivePresetId, mergedPresets.ToArray()));

        var updated = new AssortmentStoreDocument(
            CurrentSchemaVersion,
            activePresetId,
            mergedPresets.OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase).ToArray());

        return TrySaveDocument(updated, out error);
    }

    internal bool TryUpsertPreset(AssortmentPresetDocument preset, bool setActive, out string persistedPresetId, out string? error)
    {
        persistedPresetId = string.Empty;

        if (!TryBuildCatalogFromPreset(preset, out var catalog, out var extraCategories, out error))
        {
            return false;
        }

        var basePresetId = NormalizePresetId(preset.Id);
        if (string.IsNullOrWhiteSpace(basePresetId))
        {
            basePresetId = NormalizePresetId(preset.Name);
        }

        if (string.IsNullOrWhiteSpace(basePresetId))
        {
            basePresetId = "ONLINE_PRESET";
        }

        persistedPresetId = basePresetId;
        var presetName = string.IsNullOrWhiteSpace(preset.Name) ? basePresetId : preset.Name.Trim();
        return TryUpsertPreset(persistedPresetId, presetName, catalog, extraCategories, setActive, out error);
    }

    internal bool TryDeletePreset(string presetId, out string? error)
    {
        var normalizedPresetId = NormalizePresetId(presetId);
        if (string.IsNullOrWhiteSpace(normalizedPresetId))
        {
            error = "Preset id must not be empty.";
            return false;
        }

        if (!TryLoadDocument(out var document, out error) || document == null)
        {
            return false;
        }

        var remaining = document.Presets
            .Where(preset => !string.Equals(NormalizePresetId(preset.Id), normalizedPresetId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remaining.Length == document.Presets.Length)
        {
            error = $"Preset '{normalizedPresetId}' does not exist.";
            return false;
        }

        if (remaining.Length == 0)
        {
            error = "At least one preset must remain.";
            return false;
        }

        var activePresetId = ResolveActivePresetId(new AssortmentStoreDocument(
            CurrentSchemaVersion,
            string.Equals(ResolveActivePresetId(document), normalizedPresetId, StringComparison.OrdinalIgnoreCase)
                ? NormalizePresetId(remaining[0].Id)
                : ResolveActivePresetId(document),
            remaining));

        var updated = new AssortmentStoreDocument(CurrentSchemaVersion, activePresetId, remaining);
        return TrySaveDocument(updated, out error);
    }

    private bool TryWriteLegacyJsonSnapshot(AssortmentStoreDocument document, out string? error)
    {
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

    private bool TryLoadDocument(out AssortmentStoreDocument? document, out string? error)
    {
        document = null;

        string? sqliteError = null;
        if (_sqliteStore.TryLoad(out var sqliteDocument, out sqliteError) && sqliteDocument != null)
        {
            document = sqliteDocument;
            error = null;
            return true;
        }

        string? jsonError = null;
        if (File.Exists(FilePath))
        {
            if (TryReadDocument(out var jsonDocument, out jsonError) && jsonDocument != null)
            {
                document = jsonDocument;
                error = null;
                _ = _sqliteStore.TrySave(jsonDocument, out _);
                return true;
            }
        }

        error = jsonError ?? sqliteError;
        return false;
    }

    private bool TryLoadOrInitializeDocument(out AssortmentStoreDocument document, out string? error)
    {
        if (TryLoadDocument(out var loadedDocument, out error) && loadedDocument != null)
        {
            document = loadedDocument;
            error = null;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            document = new AssortmentStoreDocument(CurrentSchemaVersion, DefaultPresetId, Array.Empty<AssortmentPresetDocument>());
            return false;
        }

        document = new AssortmentStoreDocument(CurrentSchemaVersion, DefaultPresetId, Array.Empty<AssortmentPresetDocument>());
        error = null;
        return true;
    }

    private bool TrySaveDocument(AssortmentStoreDocument document, out string? error)
    {
        if (!_sqliteStore.TrySave(document, out var sqliteError))
        {
            error = $"SQLite save failed at {SqliteFilePath}: {sqliteError}";
            return false;
        }

        _ = TryWriteLegacyJsonSnapshot(document, out _);
        error = null;
        return true;
    }

    private static AssortmentPresetDocument BuildPresetDocument(
        string presetId,
        string presetName,
        IReadOnlyCollection<CatalogItemEditor> catalog,
        IReadOnlyCollection<string> extraCategories)
    {
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
                string.IsNullOrWhiteSpace(item.Name) ? item.Id.Trim() : item.Name.Trim(),
                item.UnitCents,
                string.IsNullOrWhiteSpace(item.Category) ? DefaultCategory : item.Category.Trim()))
            .ToArray();

        return new AssortmentPresetDocument(presetId, presetName, allCategories, savedItems);
    }

    private static bool TryValidateCatalog(IReadOnlyCollection<CatalogItemEditor> catalog, out string? error)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            var itemId = item.Id?.Trim();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                error = "Catalog item id must not be empty.";
                return false;
            }

            if (!seenIds.Add(itemId))
            {
                error = $"Catalog contains duplicate item id '{itemId}'.";
                return false;
            }

            if (item.UnitCents < 0)
            {
                error = $"Catalog item '{itemId}' has a negative unit_cents value.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private bool TryBuildCatalogFromDocument(
        AssortmentStoreDocument document,
        string? requestedPresetId,
        out List<CatalogItemEditor> catalog,
        out List<string> extraCategories,
        out string? error)
    {
        catalog = new List<CatalogItemEditor>();
        extraCategories = new List<string>();

        if (!TryResolvePreset(document, requestedPresetId, out var preset, out error))
        {
            return false;
        }

        return TryBuildCatalogFromPreset(preset, out catalog, out extraCategories, out error);
    }

    private bool TryResolvePreset(
        AssortmentStoreDocument document,
        string? requestedPresetId,
        out AssortmentPresetDocument preset,
        out string? error)
    {
        preset = new AssortmentPresetDocument(DefaultPresetId, DefaultPresetName, Array.Empty<string>(), Array.Empty<AssortmentPresetItemDocument>());

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

        var targetPresetId = string.IsNullOrWhiteSpace(requestedPresetId)
            ? ResolveActivePresetId(document)
            : NormalizePresetId(requestedPresetId);

        var resolved = document.Presets.FirstOrDefault(existing =>
            string.Equals(NormalizePresetId(existing.Id), targetPresetId, StringComparison.OrdinalIgnoreCase));

        if (resolved == null)
        {
            error = $"Preset '{targetPresetId}' does not exist.";
            return false;
        }

        preset = resolved;
        error = null;
        return true;
    }

    private bool TryBuildCatalogFromPreset(
        AssortmentPresetDocument preset,
        out List<CatalogItemEditor> catalog,
        out List<string> extraCategories,
        out string? error)
    {
        catalog = new List<CatalogItemEditor>();
        extraCategories = new List<string>();

        if (preset.Items == null || preset.Items.Length == 0)
        {
            error = $"Preset '{preset.Id}' has no items.";
            return false;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in preset.Items)
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
            error = $"Preset '{preset.Id}' has no valid items.";
            return false;
        }

        var knownItemCategories = new HashSet<string>(
            catalog.Select(item => item.Category),
            StringComparer.OrdinalIgnoreCase);

        if (preset.Categories != null)
        {
            foreach (var category in preset.Categories)
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

    internal static string NormalizePresetId(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return string.Empty;
        }

        var candidate = PresetIdRegex.Replace(presetId.Trim().ToUpperInvariant(), "_").Trim('_');
        return candidate;
    }

    private static string ResolveActivePresetId(AssortmentStoreDocument document)
    {
        if (document.Presets == null || document.Presets.Length == 0)
        {
            return DefaultPresetId;
        }

        var activePresetId = NormalizePresetId(document.ActivePresetId);
        if (!string.IsNullOrWhiteSpace(activePresetId) &&
            document.Presets.Any(preset => string.Equals(NormalizePresetId(preset.Id), activePresetId, StringComparison.OrdinalIgnoreCase)))
        {
            return activePresetId;
        }

        return NormalizePresetId(document.Presets[0].Id);
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

internal sealed record AssortmentPresetSummary(
    string Id,
    string Name,
    bool IsActive,
    int ItemCount);

