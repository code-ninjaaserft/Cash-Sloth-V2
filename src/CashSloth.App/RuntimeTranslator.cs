namespace CashSloth.App;

internal sealed class RuntimeTranslator
{
    private static readonly Dictionary<string, string> SharedCache = new(StringComparer.Ordinal);

    internal string TranslateCatalogText(string text, UiLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var normalized = text.Trim();
        var targetLanguage = ToTargetLanguage(language);
        if (targetLanguage == "en")
        {
            return normalized;
        }

        var cacheKey = $"{targetLanguage}|{normalized}";
        if (SharedCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (TryTranslateKnown(normalized, targetLanguage, out var knownTranslation))
        {
            SharedCache[cacheKey] = knownTranslation;
            return knownTranslation;
        }

        SharedCache[cacheKey] = normalized;
        return normalized;
    }

    private static bool TryTranslateKnown(string text, string targetLanguage, out string translation)
    {
        translation = text;

        return targetLanguage switch
        {
            "de" => TryTranslate(text, GermanKnown, out translation),
            "fr" => TryTranslate(text, FrenchKnown, out translation),
            "rm" => TryTranslate(text, RumantschKnown, out translation),
            _ => false
        };
    }

    private static bool TryTranslate(string text, IReadOnlyDictionary<string, string> dictionary, out string translation)
    {
        return dictionary.TryGetValue(text, out translation!);
    }

    private static string ToTargetLanguage(UiLanguage language)
    {
        return language switch
        {
            UiLanguage.GermanCh or UiLanguage.GermanDe => "de",
            UiLanguage.FrenchCh => "fr",
            UiLanguage.RumantschSursilvan => "rm",
            _ => "en"
        };
    }

    private static readonly IReadOnlyDictionary<string, string> GermanKnown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffee"] = "Kaffee",
        ["Tea"] = "Tee",
        ["Water"] = "Wasser",
        ["Cola"] = "Cola",
        ["Chips"] = "Chips",
        ["Cake"] = "Kuchen",
        ["Hot Drinks"] = "Heissgetraenke",
        ["Soft Drinks"] = "Erfrischungsgetraenke",
        ["Snacks"] = "Snacks"
    };

    private static readonly IReadOnlyDictionary<string, string> FrenchKnown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffee"] = "Cafe",
        ["Tea"] = "The",
        ["Water"] = "Eau",
        ["Cola"] = "Cola",
        ["Chips"] = "Chips",
        ["Cake"] = "Gateau",
        ["Hot Drinks"] = "Boissons chaudes",
        ["Soft Drinks"] = "Boissons fraiches",
        ["Snacks"] = "Snacks"
    };

    private static readonly IReadOnlyDictionary<string, string> RumantschKnown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffee"] = "Cafe",
        ["Tea"] = "Te",
        ["Water"] = "Aua",
        ["Cola"] = "Cola",
        ["Chips"] = "Chips",
        ["Cake"] = "Torta",
        ["Hot Drinks"] = "Bubrondas cauldas",
        ["Soft Drinks"] = "Bubrondas frestgas",
        ["Snacks"] = "Snacks"
    };
}
