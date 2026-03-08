using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CashSloth.App;

internal sealed class OnlinePresetProvider
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal bool TryDownloadPreset(string url, out AssortmentPresetDocument? preset, out string? error)
    {
        preset = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "Preset URL must be a valid HTTP or HTTPS URL.";
            return false;
        }

        try
        {
            var json = _httpClient.GetStringAsync(uri).GetAwaiter().GetResult();
            return TryParsePresetJson(json, out preset, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryParsePresetJson(string json, out AssortmentPresetDocument? preset, out string? error)
    {
        preset = null;

        try
        {
            var store = JsonSerializer.Deserialize<AssortmentStoreDocument>(json, _jsonOptions);
            if (store?.Presets is { Length: > 0 })
            {
                var activePresetId = AssortmentPresetStore.NormalizePresetId(store.ActivePresetId);
                preset = store.Presets.FirstOrDefault(existing =>
                    string.Equals(AssortmentPresetStore.NormalizePresetId(existing.Id), activePresetId, StringComparison.OrdinalIgnoreCase))
                    ?? store.Presets[0];
                error = null;
                return true;
            }
        }
        catch
        {
            // Ignore and continue with single-preset parsing.
        }

        try
        {
            var single = JsonSerializer.Deserialize<AssortmentPresetDocument>(json, _jsonOptions);
            if (single?.Items is { Length: > 0 })
            {
                var id = string.IsNullOrWhiteSpace(single.Id)
                    ? AssortmentPresetStore.NormalizePresetId(single.Name)
                    : AssortmentPresetStore.NormalizePresetId(single.Id);
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = "ONLINE_PRESET";
                }

                var name = string.IsNullOrWhiteSpace(single.Name) ? id : single.Name.Trim();
                var categories = (single.Categories ?? Array.Empty<string>())
                    .Select(category => category.Trim())
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var items = single.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                    .Select(item => new AssortmentPresetItemDocument(
                        item.Id.Trim(),
                        string.IsNullOrWhiteSpace(item.Name) ? item.Id.Trim() : item.Name.Trim(),
                        item.UnitCents,
                        string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category.Trim()))
                    .ToArray();

                if (items.Length == 0)
                {
                    error = "Preset JSON does not contain valid items.";
                    return false;
                }

                preset = new AssortmentPresetDocument(id, name, categories, items);
                error = null;
                return true;
            }
        }
        catch
        {
            // Ignore and continue with loose parsing.
        }

        return TryParseLoosePreset(json, out preset, out error);
    }

    private static bool TryParseLoosePreset(string json, out AssortmentPresetDocument? preset, out string? error)
    {
        preset = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Preset JSON root must be an object.";
                return false;
            }

            var root = document.RootElement;
            var id = ReadString(root, "id", "preset_id", "slug");
            var name = ReadString(root, "name", "title");

            id = string.IsNullOrWhiteSpace(id)
                ? AssortmentPresetStore.NormalizePresetId(name)
                : AssortmentPresetStore.NormalizePresetId(id);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "ONLINE_PRESET";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            if (!TryGetItemsElement(root, out var itemsElement))
            {
                error = "Preset JSON must include an 'items' or 'products' array.";
                return false;
            }

            var itemList = new List<AssortmentPresetItemDocument>();
            var categorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCategories(root, categorySet);

            var index = 1;
            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                if (itemElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var itemId = ReadString(itemElement, "id", "sku", "code");
                var itemName = ReadString(itemElement, "name", "title");

                itemId = string.IsNullOrWhiteSpace(itemId)
                    ? AssortmentPresetStore.NormalizePresetId(itemName)
                    : AssortmentPresetStore.NormalizePresetId(itemId);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    itemId = $"ITEM_{index}";
                }

                if (string.IsNullOrWhiteSpace(itemName))
                {
                    itemName = itemId;
                }

                if (!TryReadUnitCents(itemElement, out var unitCents))
                {
                    index++;
                    continue;
                }

                var category = ReadString(itemElement, "category", "group", "type");
                if (string.IsNullOrWhiteSpace(category))
                {
                    category = "General";
                }

                category = category.Trim();
                categorySet.Add(category);
                itemList.Add(new AssortmentPresetItemDocument(itemId, itemName.Trim(), unitCents, category));
                index++;
            }

            if (itemList.Count == 0)
            {
                error = "Preset JSON does not contain valid items.";
                return false;
            }

            var categories = categorySet
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            preset = new AssortmentPresetDocument(id, name.Trim(), categories, itemList.ToArray());
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryGetItemsElement(JsonElement root, out JsonElement itemsElement)
    {
        if (root.TryGetProperty("items", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (root.TryGetProperty("products", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        itemsElement = default;
        return false;
    }

    private static bool TryReadUnitCents(JsonElement itemElement, out long unitCents)
    {
        unitCents = 0;

        if (itemElement.TryGetProperty("unit_cents", out var centsElement))
        {
            if (centsElement.ValueKind == JsonValueKind.Number && centsElement.TryGetInt64(out unitCents))
            {
                return unitCents >= 0;
            }

            if (centsElement.ValueKind == JsonValueKind.String &&
                long.TryParse(centsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out unitCents))
            {
                return unitCents >= 0;
            }
        }

        if (itemElement.TryGetProperty("price", out var priceElement))
        {
            if (TryReadDecimal(priceElement, out var priceValue) && priceValue >= 0m)
            {
                unitCents = (long)decimal.Round(priceValue * 100m, 0, MidpointRounding.AwayFromZero);
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
                decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        return false;
    }

    private static void AddCategories(JsonElement root, ISet<string> categories)
    {
        if (!root.TryGetProperty("categories", out var categoriesElement) || categoriesElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var category in categoriesElement.EnumerateArray())
        {
            if (category.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = category.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            categories.Add(value);
        }
    }

    private static string ReadString(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!source.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
