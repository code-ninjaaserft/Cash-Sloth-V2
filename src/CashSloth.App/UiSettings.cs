using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Microsoft.Win32;

namespace CashSloth.App;

internal enum UiLanguage
{
    RumantschSursilvan,
    EnglishUk,
    GermanCh,
    GermanDe,
    FrenchCh
}

internal enum UiCurrency
{
    Chf,
    Eur,
    Usd,
    Gbp
}

internal enum UiThemeMode
{
    System,
    Light,
    Dark
}

internal sealed record AppSettings(UiLanguage Language, UiCurrency Currency, UiThemeMode Theme)
{
    internal static AppSettings Default { get; } = new(UiLanguage.EnglishUk, UiCurrency.Chf, UiThemeMode.System);
}

internal sealed record UiOption<T>(T Value, string Label);

internal sealed class AppSettingsStore
{
    private const int CurrentSchemaVersion = 1;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal AppSettingsStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = Path.Combine(localAppData, "CashSloth", "ui.settings.json");
    }

    internal string FilePath { get; }

    internal AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var document = JsonSerializer.Deserialize<AppSettingsDocument>(json, _jsonOptions);
            if (document == null || document.SchemaVersion > CurrentSchemaVersion)
            {
                return AppSettings.Default;
            }

            if (!Enum.TryParse<UiLanguage>(document.Language, true, out var language))
            {
                language = AppSettings.Default.Language;
            }

            if (!Enum.TryParse<UiCurrency>(document.Currency, true, out var currency))
            {
                currency = AppSettings.Default.Currency;
            }

            if (!Enum.TryParse<UiThemeMode>(document.Theme, true, out var theme))
            {
                theme = AppSettings.Default.Theme;
            }

            return new AppSettings(language, currency, theme);
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    internal bool TrySave(AppSettings settings, out string? error)
    {
        var document = new AppSettingsDocument(
            CurrentSchemaVersion,
            settings.Language.ToString(),
            settings.Currency.ToString(),
            settings.Theme.ToString());

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
}

internal sealed record ThemePalette(
    Color WindowBackground,
    Color SurfaceBackground,
    Color ControlBackground,
    Color ControlBorder,
    Color Foreground,
    Color SelectionBackground,
    Color SelectionForeground,
    Color CategorySelectionBackground,
    Color CategorySelectionBorder,
    Color EditSelectionBackground);

internal static class ThemePaletteResolver
{
    private static readonly ThemePalette Light = new(
        Color.FromRgb(246, 247, 250),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(176, 176, 176),
        Color.FromRgb(25, 25, 25),
        Color.FromRgb(0, 120, 215),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(208, 224, 245),
        Color.FromRgb(70, 130, 180),
        Color.FromRgb(252, 246, 186));

    private static readonly ThemePalette Dark = new(
        Color.FromRgb(17, 17, 17),
        Color.FromRgb(24, 24, 24),
        Color.FromRgb(31, 31, 31),
        Color.FromRgb(58, 58, 58),
        Color.FromRgb(245, 245, 245),
        Color.FromRgb(0, 90, 158),
        Color.FromRgb(255, 255, 255),
        Color.FromRgb(38, 52, 68),
        Color.FromRgb(0, 120, 212),
        Color.FromRgb(52, 52, 52));

    internal static ThemePalette Resolve(UiThemeMode preference)
    {
        var effectiveMode = preference == UiThemeMode.System
            ? DetectSystemTheme()
            : preference;

        return effectiveMode == UiThemeMode.Dark ? Dark : Light;
    }

    private static UiThemeMode DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0 ? UiThemeMode.Dark : UiThemeMode.Light;
            }
        }
        catch
        {
            // Fall back to light if registry probing fails.
        }

        return UiThemeMode.Light;
    }
}

internal static class UiLocalizer
{
    private sealed record Translation(string En, string De, string Fr, string Rm);

    private static readonly IReadOnlyDictionary<string, Translation> Translations = new Dictionary<string, Translation>(StringComparer.Ordinal)
    {
        ["main.title"] = new("CashSloth POS", "CashSloth Kasse", "CashSloth Caisse", "CashSloth POS"),
        ["customer.title"] = new("Customer Display", "Kundenanzeige", "Affichage client", "Display client"),
        ["settings.language"] = new("Language", "Sprache", "Langue", "Lingua"),
        ["settings.currency"] = new("Currency", "Waehrung", "Monnaie", "Valuta"),
        ["settings.theme"] = new("UI Color", "UI-Farbe", "Couleur UI", "Colur UI"),
        ["tab.settings"] = new("Settings", "Einstellungen", "Parametres", "Settings"),
        ["tab.presets"] = new("Presets", "Presets", "Presets", "Presets"),
        ["preset.local_presets"] = new("Local presets", "Lokale Presets", "Presets locaux", "Presets locals"),
        ["preset.online_import"] = new("Online preset import", "Online-Preset-Import", "Import preset en ligne", "Import presets online"),
        ["preset.save_current_as"] = new("Save current as", "Aktuelles speichern als", "Enregistrer actuel comme", "Salvar actual sco"),
        ["preset.url"] = new("Preset URL", "Preset-URL", "URL preset", "URL preset"),
        ["preset.optional_name"] = new("Optional preset name", "Optionaler Preset-Name", "Nom de preset optionnel", "Num preset optional"),
        ["button.switch_preset"] = new("Switch preset", "Preset wechseln", "Changer preset", "Midar preset"),
        ["button.refresh_presets"] = new("Refresh", "Aktualisieren", "Actualiser", "Actualisar"),
        ["button.delete_preset"] = new("Delete preset", "Preset loeschen", "Supprimer preset", "Stizzar preset"),
        ["button.save_current_preset"] = new("Save current preset", "Aktuelles Preset speichern", "Enregistrer preset actuel", "Salvar preset actual"),
        ["button.import_online_preset"] = new("Import online preset", "Online-Preset importieren", "Importer preset en ligne", "Importar preset online"),
        ["checkbox.set_active"] = new("Set active", "Aktiv setzen", "Definir actif", "Definir activ"),
        ["hint.online_preset_formats"] = new("Supports preset JSON and store JSON with presets.", "Unterstuetzt Preset-JSON und Store-JSON mit Presets.", "Prend en charge le JSON preset et le JSON store avec presets.", "Sustegn preset JSON e store JSON cun presets."),
        ["tooltip.preset_name_example"] = new("Preset name (e.g. Summer Menu)", "Preset-Name (z.B. Sommerkarte)", "Nom du preset (ex. Menu ete)", "Num preset (p.ex. menu stad)"),
        ["tooltip.online_preset_url"] = new("Preset JSON URL (https://...)", "Preset-JSON-URL (https://...)", "URL JSON preset (https://...)", "URL preset JSON (https://...)"),
        ["tooltip.online_preset_name"] = new("Override preset name", "Preset-Name ueberschreiben", "Remplacer le nom du preset", "Surpassar num preset"),
        ["preset.option_format"] = new("{0} ({1} items)", "{0} ({1} Artikel)", "{0} ({1} articles)", "{0} ({1} artitgels)"),
        ["preset.option_active_format"] = new("{0} ({1} items) - active", "{0} ({1} Artikel) - aktiv", "{0} ({1} articles) - actif", "{0} ({1} artitgels) - activ"),
        ["theme.system"] = new("System", "System", "Systeme", "Sistem"),
        ["theme.light"] = new("Light", "Hell", "Clair", "Cler"),
        ["theme.dark"] = new("Dark", "Dunkel", "Sombre", "Stgir"),
        ["currency.chf"] = new("CHF", "CHF", "CHF", "CHF"),
        ["currency.eur"] = new("Euro", "Euro", "Euro", "Euro"),
        ["currency.usd"] = new("Dollar", "Dollar", "Dollar", "Dollar"),
        ["currency.gbp"] = new("Pound", "Pfund", "Livre", "Pfund"),
        ["language.rm"] = new("Rumantsch Sursilvan", "Rumantsch Sursilvan", "Rumantsch Sursilvan", "Rumantsch sursilvan"),
        ["language.en"] = new("English (UK)", "Englisch (UK)", "Anglais (UK)", "Englais (UK)"),
        ["language.dech"] = new("German (CH)", "Deutsch (CH)", "Allemand (CH)", "Tudestg (CH)"),
        ["language.dede"] = new("German (DE)", "Deutsch (DE)", "Allemand (DE)", "Tudestg (DE)"),
        ["language.fr"] = new("French (CH)", "Franzoesisch (CH)", "Francais (CH)", "Franzos (CH)"),
        ["group.products"] = new("Products", "Produkte", "Produits", "Products"),
        ["group.cart"] = new("Cart", "Warenkorb", "Panier", "Panier"),
        ["header.categories"] = new("Categories", "Kategorien", "Categories", "Categorias"),
        ["header.items"] = new("Items", "Artikel", "Articles", "Artitgels"),
        ["checkbox.edit_mode"] = new("Edit mode", "Bearbeiten", "Mode edition", "Modus editar"),
        ["button.catalog_editor"] = new("Catalog Editor", "Katalog bearbeiten", "Editer catalogue", "Editar catalog"),
        ["button.add_item"] = new("Add Item", "Artikel hinzufuegen", "Ajouter article", "Agiuntar artitgel"),
        ["button.categories"] = new("Categories", "Kategorien", "Categories", "Categorias"),
        ["column.item"] = new("Item", "Artikel", "Article", "Artitgel"),
        ["column.qty"] = new("Qty", "Menge", "Qtte", "Quantitad"),
        ["column.line_total"] = new("Line Total", "Zeilensumme", "Total ligne", "Total lingia"),
        ["button.remove_selected"] = new("Remove Selected", "Auswahl entfernen", "Supprimer selection", "Stizzar selecziun"),
        ["button.clear"] = new("Clear", "Leeren", "Vider", "Vidar"),
        ["button.open_customer_display"] = new("Open Customer Display", "Kundenanzeige oeffnen", "Ouvrir affichage client", "Avrir display client"),
        ["button.close_customer_display"] = new("Close Customer Display", "Kundenanzeige schliessen", "Fermer affichage client", "Serrar display client"),
        ["label.given_colon"] = new("Given:", "Gegeben:", "Recu:", "Dau:"),
        ["button.given_reset"] = new("Given reset", "Gegeben zuruecksetzen", "Recu reinitialiser", "Reset dau"),
        ["tooltip.custom_given"] = new("Custom amount (e.g. 12.50)", "Betrag frei (z.B. 12.50)", "Montant libre (ex. 12.50)", "Ammount liber (p.ex. 12.50)"),
        ["button.add_custom"] = new("Add Custom", "Betrag addieren", "Ajouter montant", "Agiuntar summa"),
        ["label.total"] = new("Total", "Total", "Total", "Total"),
        ["label.given"] = new("Given", "Gegeben", "Recu", "Dau"),
        ["label.change"] = new("Change", "Rueckgeld", "Monnaie", "Restit."),
        ["hint.exact_amount"] = new("Exact amount", "Exakter Betrag", "Montant exact", "Exact"),
        ["hint.missing_format"] = new("Missing {0}", "Fehlen {0}", "Manque {0}", "Muncan {0}"),
        ["hint.return_format"] = new("Return {0}", "Rueckgabe {0}", "Rendre {0}", "Returnar {0}"),
        ["overlay.catalog_editor_title"] = new("Catalog Editor", "Katalog bearbeiten", "Editer catalogue", "Editar catalog"),
        ["label.existing_items"] = new("Existing items", "Vorhandene Artikel", "Articles existants", "Artitgels existents"),
        ["label.id"] = new("ID:", "ID:", "ID:", "ID:"),
        ["label.name"] = new("Name:", "Name:", "Nom:", "Num:"),
        ["label.price"] = new("Price:", "Preis:", "Prix:", "Prezi:"),
        ["label.category"] = new("Category:", "Kategorie:", "Categorie:", "Categoria:"),
        ["button.save"] = new("Save", "Speichern", "Enregistrer", "Salvar"),
        ["button.delete_item"] = new("Delete Item", "Artikel loeschen", "Supprimer article", "Stizzar artitgel"),
        ["button.close"] = new("Close", "Schliessen", "Fermer", "Serrar"),
        ["overlay.add_item_title"] = new("Add Item", "Artikel hinzufuegen", "Ajouter article", "Agiuntar artitgel"),
        ["button.create"] = new("Create", "Erstellen", "Creer", "Crear"),
        ["button.cancel"] = new("Cancel", "Abbrechen", "Annuler", "Interrumper"),
        ["overlay.category_manager_title"] = new("Edit Mode On - Categories", "Bearbeiten aktiv - Kategorien", "Mode edition actif - Categories", "Modus editar activ - categorias"),
        ["button.add_category_ellipsis"] = new("Add Category...", "Kategorie hinzufuegen...", "Ajouter categorie...", "Agiuntar categoria..."),
        ["overlay.add_category_title"] = new("Add Category", "Kategorie hinzufuegen", "Ajouter categorie", "Agiuntar categoria"),
        ["tooltip.category_name"] = new("Category name", "Kategoriename", "Nom de categorie", "Num da categoria"),
        ["text.no_items_in_category"] = new("No items in this category.", "Keine Artikel in dieser Kategorie.", "Aucun article dans cette categorie.", "Negin artitgel en questa categoria."),
        ["category.all"] = new("All", "Alle", "Tous", "Tut"),
        ["status.initializing_core"] = new("Initializing core...", "Core wird initialisiert...", "Initialisation du core...", "Inizialisar core..."),
        ["status.selected_product_missing"] = new("Selected product does not exist anymore.", "Das gewaehlte Produkt existiert nicht mehr.", "Le produit selectionne n'existe plus.", "Product selecziunau exista buca pli."),
        ["status.enable_edit_mode_first"] = new("Enable edit mode first.", "Bitte zuerst Bearbeiten aktivieren.", "Activez d'abord le mode edition.", "Activescha empriu il modus editar."),
        ["status.category_name_required"] = new("Category name is required.", "Kategoriename ist erforderlich.", "Le nom de categorie est requis.", "Num da categoria ei necessari."),
        ["status.category_added_saved_failed"] = new("Category '{0}' added, but assortment JSON could not be saved: {1}", "Kategorie '{0}' hinzugefuegt, aber Assortment-JSON konnte nicht gespeichert werden: {1}", "Categorie '{0}' ajoutee, mais le JSON d'assortiment n'a pas pu etre enregistre: {1}", "Categoria '{0}' agiuntada, denton JSON d'assortiment buca savegiau: {1}"),
        ["status.category_added"] = new("Category '{0}' added.", "Kategorie '{0}' hinzugefuegt.", "Categorie '{0}' ajoutee.", "Categoria '{0}' agiuntada."),
        ["status.category_has_items"] = new("Category '{0}' has items. Delete or move them first.", "Kategorie '{0}' enthaelt Artikel. Bitte zuerst loeschen oder verschieben.", "La categorie '{0}' contient des articles. Supprimez-les ou deplacez-les d'abord.", "Categoria '{0}' ha artitgels. Stizzar ni spustar els emprema."),
        ["status.category_cannot_remove"] = new("Category '{0}' cannot be removed.", "Kategorie '{0}' kann nicht entfernt werden.", "La categorie '{0}' ne peut pas etre supprimee.", "Categoria '{0}' sa buca vegnir stizzada."),
        ["status.category_removed_saved_failed"] = new("Category '{0}' removed, but assortment JSON could not be saved: {1}", "Kategorie '{0}' entfernt, aber Assortment-JSON konnte nicht gespeichert werden: {1}", "Categorie '{0}' supprimee, mais le JSON d'assortiment n'a pas pu etre enregistre: {1}", "Categoria '{0}' stizzada, denton JSON d'assortiment buca savegiau: {1}"),
        ["status.category_removed"] = new("Category '{0}' removed.", "Kategorie '{0}' entfernt.", "Categorie '{0}' supprimee.", "Categoria '{0}' stizzada."),
        ["status.select_product_edit"] = new("Select a product to edit.", "Bitte ein Produkt zum Bearbeiten waehlen.", "Selectionnez un produit a modifier.", "Selecziunescha in product per editar."),
        ["status.product_updated"] = new("Product updated. Cart reset.", "Produkt aktualisiert. Warenkorb zurueckgesetzt.", "Produit mis a jour. Panier reinitialise.", "Product actualisaus. Panier resetaus."),
        ["status.select_product_delete"] = new("Select a product to delete.", "Bitte ein Produkt zum Loeschen waehlen.", "Selectionnez un produit a supprimer.", "Selecziunescha in product per stizzar."),
        ["status.at_least_one_product"] = new("At least one product must remain.", "Mindestens ein Produkt muss bleiben.", "Au moins un produit doit rester.", "Almain in product sto restar."),
        ["status.product_deleted"] = new("Product deleted. Cart reset.", "Produkt geloescht. Warenkorb zurueckgesetzt.", "Produit supprime. Panier reinitialise.", "Product stizzau. Panier resetaus."),
        ["status.select_line_remove"] = new("Select a cart line to remove.", "Bitte eine Warenkorbzeile zum Entfernen waehlen.", "Selectionnez une ligne du panier a supprimer.", "Selecziunescha ina lingia da panier per stizzar."),
        ["status.custom_amount_invalid"] = new("Custom amount must be a valid value greater than 0.", "Der freie Betrag muss gueltig und groesser als 0 sein.", "Le montant libre doit etre valide et superieur a 0.", "Ammount liber sto esser valid e pli gronds che 0."),
        ["status.cart_json_empty"] = new("Cart JSON returned empty.", "Warenkorb-JSON war leer.", "Le JSON du panier est vide.", "JSON dal panier ei vits."),
        ["status.cart_json_unreadable"] = new("Unable to read cart JSON.", "Warenkorb-JSON konnte nicht gelesen werden.", "Impossible de lire le JSON du panier.", "Impusseivel da leger JSON dal panier."),
        ["status.failed_parse_cart_json"] = new("Failed to parse cart JSON: {0}", "Warenkorb-JSON konnte nicht geparst werden: {0}", "Echec lors de l'analyse du JSON du panier: {0}", "Betg reussiu da parsear JSON dal panier: {0}"),
        ["status.cart_not_ready"] = new("Cart is not ready yet.", "Warenkorb ist noch nicht bereit.", "Le panier n'est pas encore pret.", "Panier aunc buca promts."),
        ["status.unknown_error"] = new("Unknown error.", "Unbekannter Fehler.", "Erreur inconnue.", "Errur nunenconuschenta."),
        ["status.failed_action"] = new("Failed to {0} ({1}): {2}", "{0} fehlgeschlagen ({1}): {2}", "Echec de {0} ({1}) : {2}", "Betg reussiu da {0} ({1}): {2}"),
        ["status.assortment_not_saved"] = new("{0} Assortment JSON was not saved: {1}", "{0} Assortment-JSON wurde nicht gespeichert: {1}", "{0} Le JSON d'assortiment n'a pas ete enregistre: {1}", "{0} JSON d'assortiment buca savegiau: {1}"),
        ["status.name_required"] = new("Name is required.", "Name ist erforderlich.", "Le nom est requis.", "Num ei necessari."),
        ["status.price_invalid"] = new("Price must be a valid amount (e.g. 4.50).", "Preis muss ein gueltiger Betrag sein (z.B. 4.50).", "Le prix doit etre un montant valide (ex. 4.50).", "Prezi sto esser in ammount valid (p.ex. 4.50)."),
        ["status.product_added"] = new("New product added. Cart reset.", "Neues Produkt hinzugefuegt. Warenkorb zurueckgesetzt.", "Nouveau produit ajoute. Panier reinitialise.", "Niev product agiuntaus. Panier resetaus."),
        ["status.using_default_assortment"] = new("Using default assortment. Failed to load {0}: {1}", "Standardassortiment wird verwendet. Laden fehlgeschlagen {0}: {1}", "Assortiment par defaut utilise. Echec du chargement {0}: {1}", "Assortiment standard en diever. Cargar buca reussiu {0}: {1}"),
        ["status.created_assortment_backend"] = new("Created assortment backend: {0}", "Assortment-Backend erstellt: {0}", "Backend d'assortiment cree: {0}", "Backend d'assortiment creau: {0}"),
        ["status.failed_to_create_assortment"] = new("Using default assortment. Failed to create {0}: {1}", "Standardassortiment wird verwendet. Erstellen fehlgeschlagen {0}: {1}", "Assortiment par defaut utilise. Creation echouee {0}: {1}", "Assortiment standard en diever. Crear buca reussiu {0}: {1}"),
        ["tooltip.add_item_in_category"] = new("Add new item in category '{0}'", "Neuen Artikel in Kategorie '{0}' hinzufuegen", "Ajouter un article dans la categorie '{0}'", "Agiuntar artitgel niev ella categoria '{0}'"),
        ["tooltip.delete_category"] = new("Delete category '{0}'", "Kategorie '{0}' loeschen", "Supprimer la categorie '{0}'", "Stizzar categoria '{0}'"),
        ["status.settings_save_failed"] = new("UI settings could not be saved: {0}", "UI-Einstellungen konnten nicht gespeichert werden: {0}", "Les parametres UI n'ont pas pu etre enregistres: {0}", "Settings UI buca savegai: {0}"),
        ["status.presets_load_failed"] = new("Preset list could not be loaded: {0}", "Preset-Liste konnte nicht geladen werden: {0}", "La liste des presets n'a pas pu etre chargee: {0}", "Gliesta presets buca cargada: {0}"),
        ["status.preset_select_required"] = new("Select a preset first.", "Bitte zuerst ein Preset waehlen.", "Selectionnez d'abord un preset.", "Selecziunescha emprema in preset."),
        ["status.preset_switch_failed"] = new("Preset switch failed: {0}", "Preset-Wechsel fehlgeschlagen: {0}", "Echec du changement de preset: {0}", "Midada preset buca reussiu: {0}"),
        ["status.preset_switched"] = new("Switched to preset '{0}'.", "Zu Preset '{0}' gewechselt.", "Bascule vers preset '{0}'.", "Midiu sin preset '{0}'."),
        ["status.preset_name_required"] = new("Preset name is required.", "Preset-Name ist erforderlich.", "Le nom du preset est requis.", "Num preset ei necessari."),
        ["status.preset_saved"] = new("Preset '{0}' saved.", "Preset '{0}' gespeichert.", "Preset '{0}' enregistre.", "Preset '{0}' salvaus."),
        ["status.preset_save_failed"] = new("Preset could not be saved: {0}", "Preset konnte nicht gespeichert werden: {0}", "Le preset n'a pas pu etre enregistre: {0}", "Preset buca savegiaus: {0}"),
        ["status.preset_delete_failed"] = new("Preset could not be deleted: {0}", "Preset konnte nicht geloescht werden: {0}", "Le preset n'a pas pu etre supprime: {0}", "Preset buca stizzaus: {0}"),
        ["status.preset_deleted"] = new("Preset '{0}' deleted.", "Preset '{0}' geloescht.", "Preset '{0}' supprime.", "Preset '{0}' stizzaus."),
        ["status.preset_url_required"] = new("Preset URL is required.", "Preset-URL ist erforderlich.", "L'URL du preset est requise.", "URL preset ei necessaria."),
        ["status.preset_import_failed"] = new("Online preset import failed: {0}", "Online-Preset-Import fehlgeschlagen: {0}", "Import de preset en ligne echoue: {0}", "Import preset online buca reussiu: {0}"),
        ["status.preset_imported"] = new("Online preset '{0}' imported.", "Online-Preset '{0}' importiert.", "Preset en ligne '{0}' importe.", "Preset online '{0}' importaus."),
        ["status.preset_imported_and_switched"] = new("Online preset '{0}' imported and activated.", "Online-Preset '{0}' importiert und aktiviert.", "Preset en ligne '{0}' importe et active.", "Preset online '{0}' importa e activaus.")
    };

    private static readonly IReadOnlyDictionary<string, string> LiteralLookup = BuildLiteralLookup();

    internal static CultureInfo GetCulture(UiLanguage language)
    {
        var cultureName = language switch
        {
            UiLanguage.RumantschSursilvan => "rm-CH",
            UiLanguage.GermanCh => "de-CH",
            UiLanguage.GermanDe => "de-DE",
            UiLanguage.FrenchCh => "fr-CH",
            _ => "en-GB"
        };

        return CultureInfo.GetCultureInfo(cultureName);
    }

    internal static string Get(UiLanguage language, string key)
    {
        if (!Translations.TryGetValue(key, out var translation))
        {
            return key;
        }

        return ResolveLanguage(language) switch
        {
            "de" => translation.De,
            "fr" => translation.Fr,
            "rm" => translation.Rm,
            _ => translation.En
        };
    }

    internal static string Format(UiLanguage language, string key, params object[] args)
    {
        return string.Format(GetCulture(language), Get(language, key), args);
    }

    internal static string TranslateLiteral(UiLanguage language, string literal)
    {
        var normalized = NormalizeLiteral(literal);
        if (string.IsNullOrWhiteSpace(normalized) || !LiteralLookup.TryGetValue(normalized, out var key))
        {
            return literal;
        }

        return Get(language, key);
    }

    internal static IReadOnlyList<UiOption<UiLanguage>> BuildLanguageOptions(UiLanguage language)
    {
        return new[]
        {
            new UiOption<UiLanguage>(UiLanguage.RumantschSursilvan, Get(language, "language.rm")),
            new UiOption<UiLanguage>(UiLanguage.EnglishUk, Get(language, "language.en")),
            new UiOption<UiLanguage>(UiLanguage.GermanCh, Get(language, "language.dech")),
            new UiOption<UiLanguage>(UiLanguage.GermanDe, Get(language, "language.dede")),
            new UiOption<UiLanguage>(UiLanguage.FrenchCh, Get(language, "language.fr"))
        };
    }

    internal static IReadOnlyList<UiOption<UiCurrency>> BuildCurrencyOptions(UiLanguage language)
    {
        return new[]
        {
            new UiOption<UiCurrency>(UiCurrency.Chf, Get(language, "currency.chf")),
            new UiOption<UiCurrency>(UiCurrency.Eur, Get(language, "currency.eur")),
            new UiOption<UiCurrency>(UiCurrency.Usd, Get(language, "currency.usd")),
            new UiOption<UiCurrency>(UiCurrency.Gbp, Get(language, "currency.gbp"))
        };
    }

    internal static IReadOnlyList<UiOption<UiThemeMode>> BuildThemeOptions(UiLanguage language)
    {
        return new[]
        {
            new UiOption<UiThemeMode>(UiThemeMode.System, Get(language, "theme.system")),
            new UiOption<UiThemeMode>(UiThemeMode.Light, Get(language, "theme.light")),
            new UiOption<UiThemeMode>(UiThemeMode.Dark, Get(language, "theme.dark"))
        };
    }

    private static string ResolveLanguage(UiLanguage language)
    {
        return language switch
        {
            UiLanguage.GermanCh or UiLanguage.GermanDe => "de",
            UiLanguage.FrenchCh => "fr",
            UiLanguage.RumantschSursilvan => "rm",
            _ => "en"
        };
    }

    private static Dictionary<string, string> BuildLiteralLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in Translations)
        {
            TryAddLiteral(lookup, value.En, key);
            TryAddLiteral(lookup, value.De, key);
            TryAddLiteral(lookup, value.Fr, key);
            TryAddLiteral(lookup, value.Rm, key);
        }

        // Legacy literals that may still exist in older XAML/text nodes.
        TryAddLiteral(lookup, "Price CHF:", "label.price");
        TryAddLiteral(lookup, "Custom amount in CHF (e.g. 12.50)", "tooltip.custom_given");

        return lookup;
    }

    private static void TryAddLiteral(IDictionary<string, string> lookup, string literal, string key)
    {
        var normalized = NormalizeLiteral(literal);
        if (string.IsNullOrWhiteSpace(normalized) || lookup.ContainsKey(normalized))
        {
            return;
        }

        lookup[normalized] = key;
    }

    private static string NormalizeLiteral(string literal)
    {
        return literal.Trim();
    }
}

internal sealed record AppSettingsDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("theme")] string Theme);
