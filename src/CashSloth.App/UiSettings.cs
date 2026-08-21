using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CashSloth.Contracts;
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

internal sealed record AppSettings(UiLanguage Language, UiCurrency Currency, UiThemeMode Theme, bool HasSeenOnboarding)
{
    internal static AppSettings Default { get; } = new(UiLanguage.EnglishUk, UiCurrency.Chf, UiThemeMode.System, false);
}

internal sealed record UiOption<T>(T Value, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

internal sealed class AppSettingsStore
{
    private const int CurrentSchemaVersion = 2;

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

            return new AppSettings(language, currency, theme, document.HasSeenOnboarding);
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
            settings.Theme.ToString(),
            settings.HasSeenOnboarding);

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
        ["button.show_tutorial"] = new("Show tutorial", "Tutorial anzeigen", "Afficher tutoriel", "Mussar tutorial"),
        ["startup.subtitle"] = new("Point of Sale", "Kassensystem", "Point de vente", "Punct da vendita"),
        ["tab.shop"] = new("Shop", "Shop", "Vente", "Shop"),
        ["tab.settings"] = new("Settings", "Einstellungen", "Parametres", "Settings"),
        ["tab.presets"] = new("Presets", "Presets", "Presets", "Presets"),
        ["tab.accounts"] = new("Accounts", "Accounts", "Comptes", "Accounts"),
        ["tab.event"] = new("Event", "Event", "Evenement", "Occurrenza"),
        ["tab.history"] = new("History", "Verlauf", "Historique", "Historia"),
        ["onboarding.title"] = new("CashSloth quick start", "CashSloth Schnellstart", "Demarrage rapide CashSloth", "Start spert CashSloth"),
        ["onboarding.subtitle"] = new("Set up the register, sell from the Shop tab, and use Event mode when several tills work together.", "Kasse einrichten, im Shop verkaufen und Event-Modus nutzen, wenn mehrere Kassen zusammenarbeiten.", "Configurer la caisse, vendre dans l'onglet Vente et utiliser le mode evenement avec plusieurs caisses.", "Configurar la cassa, vender el tab Shop ed usar il modus event cun pliras cassas."),
        ["onboarding.shop_title"] = new("1. Shop", "1. Shop", "1. Vente", "1. Shop"),
        ["onboarding.shop_body"] = new("Use product buttons to add items. Change quantities directly in the cart with - and +, or tap the quantity number for exact input.", "Artikel ueber Produktbuttons hinzufuegen. Mengen direkt im Warenkorb mit - und + aendern oder die Menge antippen fuer exakte Eingabe.", "Ajouter des articles avec les boutons produit. Modifier les quantites avec - et +, ou toucher le nombre pour une saisie exacte.", "Agiuntar products cun ils buttons. Midar quantitads cun - e + ni tutgar il numer per endatar exact."),
        ["onboarding.checkout_title"] = new("2. Checkout", "2. Zahlung", "2. Paiement", "2. Pajament"),
        ["onboarding.checkout_body"] = new("Use quick tender buttons or a custom amount, then complete the sale. Tips and payment method are set in Event.", "Schnellbetraege oder freien Betrag nutzen, dann Verkauf abschliessen. Trinkgeld und Zahlungsart werden im Event gesetzt.", "Utiliser les montants rapides ou un montant libre, puis terminer la vente. Pourboire et mode de paiement se reglent dans Evenement.", "Usar imports sperts ni in import liber, lu terminar la vendita. Bubronda e metoda da pajament vegnan mess en Event."),
        ["onboarding.event_title"] = new("3. Event mode", "3. Event-Modus", "3. Mode evenement", "3. Modus event"),
        ["onboarding.event_body"] = new("Name the event and register. Hosts can scan for client registers; clients can make themselves visible on the network.", "Event und Kasse benennen. Hosts koennen Client-Kassen suchen; Clients koennen sich im Netzwerk sichtbar machen.", "Nommer l'evenement et la caisse. Les hotes cherchent des caisses clientes; les clients peuvent etre visibles sur le reseau.", "Numnar event e cassa. Hosts anflan cassas client; clients san daventar veseivels el network."),
        ["onboarding.accounts_title"] = new("4. Accounts and presets", "4. Accounts und Presets", "4. Comptes et presets", "4. Accounts e presets"),
        ["onboarding.accounts_body"] = new("Paired devices use central accounts and presets. Admins approve accounts and manage roles. The tutorial can be opened again from Settings.", "Gekoppelte Geraete verwenden zentrale Accounts und Presets. Admins geben Accounts frei und verwalten Rollen. Das Tutorial kann in Einstellungen erneut geoeffnet werden.", "Les appareils couples utilisent les comptes et presets centraux. Les admins approuvent les comptes et gerent les roles. Le tutoriel se rouvre dans Parametres.", "Geraets colligiai drovan accounts e presets centrals. Admins lubeschan accounts ed administreschan rollas. Il tutorial ei en Settings puspei avierts."),
        ["onboarding.localization_note"] = new("Changing language, currency, or theme affects fixed app UI only. Product names, categories, usernames, and passwords stay as entered.", "Sprache, Waehrung und Theme betreffen nur fixe App-Oberflaechen. Produktnamen, Kategorien, Usernamen und Passwoerter bleiben wie eingegeben.", "Langue, monnaie et theme changent seulement l'interface fixe. Produits, categories, utilisateurs et mots de passe restent saisis.", "Lingua, valuta e theme midan mo la UI fixa. Products, categorias, usernames e passwords restan sco endatai."),
        ["button.start_using_cashsloth"] = new("Start using CashSloth", "CashSloth starten", "Commencer avec CashSloth", "Cumenzer cun CashSloth"),
        ["account.session"] = new("Session", "Sitzung", "Session", "Sessiun"),
        ["account.not_signed_in"] = new("Not signed in", "Nicht angemeldet", "Non connecte", "Buca annunziau"),
        ["account.username"] = new("Username", "Benutzername", "Nom utilisateur", "Username"),
        ["account.password"] = new("Password", "Passwort", "Mot de passe", "Password"),
        ["account.confirm_password"] = new("Confirm password", "Passwort bestaetigen", "Confirmer mot de passe", "Confirmar password"),
        ["account.create_account"] = new("Create account", "Account erstellen", "Creer compte", "Crear account"),
        ["account.management_admin"] = new("Account management (Admin)", "Account-Verwaltung (Admin)", "Gestion comptes (Admin)", "Administraziun accounts (Admin)"),
        ["account.central_setup"] = new("Central server setup", "Central Server einrichten", "Configuration du serveur central", "Configurar server central"),
        ["account.central_sign_in"] = new("Central sign in", "Central anmelden", "Connexion centrale", "Login central"),
        ["account.need_account"] = new("Need an account?", "Noch keinen Account?", "Besoin d'un compte ?", "Drovas in account?"),
        ["account.your_central_account"] = new("Your central account", "Dein Central Account", "Votre compte central", "Tes account central"),
        ["account.role_access_hint"] = new("Role-based access is managed by the central server.", "Rollen und Rechte werden vom Central Server verwaltet.", "Les roles et les acces sont geres par le serveur central.", "Rollas ed access vegnan administrai dil server central."),
        ["account.change_password"] = new("Change password", "Passwort aendern", "Changer le mot de passe", "Midar password"),
        ["account.current_password"] = new("Current password", "Aktuelles Passwort", "Mot de passe actuel", "Password actual"),
        ["account.new_password"] = new("New password", "Neues Passwort", "Nouveau mot de passe", "Password niev"),
        ["button.update_password"] = new("Update password", "Passwort aktualisieren", "Mettre a jour le mot de passe", "Actualisar password"),
        ["label.device_name"] = new("Device name", "Geraetename", "Nom de l'appareil", "Num dil geraet"),
        ["label.pairing_code"] = new("10-character pairing code", "10-stelliger Pairing-Code", "Code de couplage a 10 caracteres", "Code da pairing da 10 caracters"),
        ["button.pair_device"] = new("Pair this device", "Dieses Geraet koppeln", "Coupler cet appareil", "Colligiar quei geraet"),
        ["account.no_server_trusted"] = new("No server trusted", "Kein Server als vertrauenswuerdig hinterlegt", "Aucun serveur approuve", "Negin server fidau"),
        ["account.offline_not_configured"] = new("Offline / not configured", "Offline / nicht eingerichtet", "Hors ligne / non configure", "Offline / buca configurau"),
        ["button.login"] = new("Login", "Anmelden", "Connexion", "Login"),
        ["button.logout"] = new("Logout", "Abmelden", "Deconnexion", "Logout"),
        ["button.create_normal_user"] = new("Create normal user", "Normalen User erstellen", "Creer utilisateur normal", "Crear user normal"),
        ["button.refresh_accounts"] = new("Refresh accounts", "Accounts aktualisieren", "Actualiser comptes", "Actualisar accounts"),
        ["button.save_account"] = new("Save account", "Account speichern", "Enregistrer compte", "Salvar account"),
        ["button.reset_password"] = new("Reset password", "Passwort zuruecksetzen", "Reinitialiser le mot de passe", "Resetar password"),
        ["label.role"] = new("Role", "Rolle", "Role", "Rolla"),
        ["role.user"] = new("User", "User", "Utilisateur", "User"),
        ["role.creator"] = new("Creator", "Ersteller", "Createur", "Creatur"),
        ["role.admin"] = new("Admin", "Admin", "Admin", "Admin"),
        ["checkbox.enabled"] = new("Enabled", "Aktiviert", "Active", "Activau"),
        ["checkbox.approved"] = new("Approved", "Freigegeben", "Approuve", "Lubiu"),
        ["event.context"] = new("Event context", "Event-Kontext", "Contexte evenement", "Context event"),
        ["event.name"] = new("Event name", "Eventname", "Nom evenement", "Num event"),
        ["event.register"] = new("Register", "Kasse", "Caisse", "Cassa"),
        ["event.checkout"] = new("Checkout", "Checkout", "Paiement", "Checkout"),
        ["event.payment_method"] = new("Payment method", "Zahlungsart", "Mode de paiement", "Metoda da pajament"),
        ["event.tip_amount"] = new("Tip amount", "Trinkgeld", "Pourboire", "Bubronda"),
        ["event.showcase_mode"] = new("Showcase mode (exclude from statistics)", "Showcase-Modus (aus Statistik ausschliessen)", "Mode showcase (exclure des statistiques)", "Modus showcase (excluder dalla statistica)"),
        ["event.host_add_client"] = new("Host: add client register", "Host: Client-Kasse hinzufuegen", "Hote: ajouter caisse cliente", "Host: agiuntar cassa client"),
        ["event.visible_registers"] = new("Visible registers", "Sichtbare Kassen", "Caisses visibles", "Cassas veseivlas"),
        ["event.network_client_mode"] = new("Network client mode", "Netzwerk-Client-Modus", "Mode client reseau", "Modus client network"),
        ["event.network_idle"] = new("Network mode idle.", "Netzwerkmodus inaktiv.", "Mode reseau inactif.", "Modus network inactiv."),
        ["event.totals"] = new("Event totals", "Event gesamt", "Totaux evenement", "Totals event"),
        ["event.selected_register"] = new("Selected register", "Ausgewaehlte Kasse", "Caisse selectionnee", "Cassa selecziunada"),
        ["button.complete_sale"] = new("Complete sale", "Verkauf abschliessen", "Terminer vente", "Terminar vendita"),
        ["button.add_additional_register"] = new("Add additional register", "Zusaetzliche Kasse hinzufuegen", "Ajouter caisse", "Agiuntar cassa"),
        ["button.add_selected_client"] = new("Add selected client", "Ausgewaehlten Client hinzufuegen", "Ajouter client selectionne", "Agiuntar client selecziunau"),
        ["button.show_register_network"] = new("Show this register on network", "Diese Kasse im Netzwerk anzeigen", "Afficher cette caisse sur le reseau", "Mussar questa cassa el network"),
        ["button.hide_register_network"] = new("Hide this register on network", "Diese Kasse im Netzwerk ausblenden", "Masquer cette caisse du reseau", "Zuppentar questa cassa el network"),
        ["button.refresh_clients"] = new("Refresh clients", "Clients aktualisieren", "Actualiser clients", "Actualisar clients"),
        ["button.remove_selected_small"] = new("Remove selected", "Auswahl entfernen", "Supprimer selection", "Stizzar selecziun"),
        ["payment.cash"] = new("Cash", "Bar", "Especes", "Cash"),
        ["payment.card"] = new("Card", "Karte", "Carte", "Carta"),
        ["payment.rfid_nfc"] = new("RFID/NFC", "RFID/NFC", "RFID/NFC", "RFID/NFC"),
        ["payment.twint"] = new("TWINT", "TWINT", "TWINT", "TWINT"),
        ["payment.mobile"] = new("Mobile", "Mobile", "Mobile", "Mobile"),
        ["history.recent_sales"] = new("Recent sales", "Letzte Verkaeufe", "Ventes recentes", "Venditas novas"),
        ["history.statistics_filters"] = new("Statistics filters", "Statistikfilter", "Filtres statistiques", "Filters statistica"),
        ["history.event"] = new("Event", "Event", "Evenement", "Event"),
        ["history.user"] = new("User", "User", "Utilisateur", "User"),
        ["history.sales"] = new("Sales", "Verkaeufe", "Ventes", "Venditas"),
        ["history.subtotal"] = new("Subtotal", "Zwischensumme", "Sous-total", "Subtotal"),
        ["history.tips"] = new("Tips", "Trinkgeld", "Pourboires", "Bubrondas"),
        ["history.lines"] = new("Lines", "Zeilen", "Lignes", "Lingias"),
        ["button.refresh_history"] = new("Refresh history", "Verlauf aktualisieren", "Actualiser historique", "Actualisar historia"),
        ["button.use_current_event"] = new("Use current event", "Aktuelles Event verwenden", "Utiliser evenement actuel", "Usar event actual"),
        ["checkbox.include_showcase"] = new("Include showcase", "Showcase einbeziehen", "Inclure showcase", "Includer showcase"),
        ["preset.local_presets"] = new("Local presets", "Lokale Presets", "Presets locaux", "Presets locals"),
        ["preset.central_presets"] = new("Central presets", "Zentrale Presets", "Presets centraux", "Presets centrals"),
        ["preset.installed_presets"] = new("Installed presets", "Installierte Presets", "Presets installes", "Presets installai"),
        ["preset.online_presets"] = new("Online presets", "Online-Presets", "Presets en ligne", "Presets online"),
        ["preset.items"] = new("items", "Artikel", "articles", "artitgels"),
        ["preset.active"] = new("Active", "Aktiv", "Actif", "Activ"),
        ["preset.active_on_server"] = new("Active on server", "Auf Server aktiv", "Actif sur le serveur", "Activ sil server"),
        ["preset.create_local"] = new("Create local preset", "Lokales Preset erstellen", "Creer un preset local", "Crear preset local"),
        ["preset.create_local_hint"] = new("Copies the current shop catalog into a new installed preset.", "Kopiert den aktuellen Shop-Katalog in ein neues installiertes Preset.", "Copie le catalogue actuel dans un nouveau preset installe.", "Copiescha il catalog actual en in niev preset installau."),
        ["preset.local_name_optional"] = new("Local name (optional)", "Lokaler Name (optional)", "Nom local (facultatif)", "Num local (optional)"),
        ["preset.publish_creator"] = new("Publish preset (Creator)", "Preset veroeffentlichen (Creator)", "Publier le preset (Creator)", "Publicar preset (Creator)"),
        ["preset.publish_hint"] = new("Uploads the selected installed preset to the central server.", "Laedt das ausgewaehlte installierte Preset auf den Central Server.", "Envoie le preset installe selectionne au serveur central.", "Carga il preset installau selecziunau sil server central."),
        ["hint.central_preset_sign_in"] = new("Sign in to browse and install presets from the central server.", "Melde dich an, um Presets vom Central Server anzusehen und zu installieren.", "Connectez-vous pour parcourir et installer les presets du serveur central.", "S'annunzia per veser ed installar presets dil server central."),
        ["hint.central_preset_change_password"] = new("Change the temporary password to access central presets.", "Aendere das temporaere Passwort, um auf zentrale Presets zuzugreifen.", "Changez le mot de passe temporaire pour acceder aux presets centraux.", "Mida il password temporar per acceder als presets centrals."),
        ["hint.central_preset_no_access"] = new("Your account cannot access central presets.", "Dein Account hat keinen Zugriff auf zentrale Presets.", "Votre compte ne peut pas acceder aux presets centraux.", "Tes account ha negin access als presets centrals."),
        ["preset.save_current_as"] = new("Save current as", "Aktuelles speichern als", "Enregistrer actuel comme", "Salvar actual sco"),
        ["preset.server_url"] = new("Central server URL", "Central-Server-URL", "URL du serveur central", "URL server central"),
        ["preset.central_list"] = new("Central preset list", "Zentrale Preset-Liste", "Liste des presets centraux", "Gliesta presets centrals"),
        ["preset.optional_name"] = new("Optional preset name", "Optionaler Preset-Name", "Nom de preset optionnel", "Num preset optional"),
        ["button.switch_preset"] = new("Switch preset", "Preset wechseln", "Changer preset", "Midar preset"),
        ["button.activate"] = new("Activate", "Aktivieren", "Activer", "Activar"),
        ["button.edit_in_shop"] = new("Edit in shop", "Im Shop bearbeiten", "Modifier dans la vente", "Editar el shop"),
        ["button.create_from_shop"] = new("Create from current shop", "Aus aktuellem Shop erstellen", "Creer depuis la vente actuelle", "Crear ord il shop actual"),
        ["button.install_selected_preset"] = new("Install selected preset", "Ausgewaehltes Preset installieren", "Installer le preset selectionne", "Installar preset selecziunau"),
        ["button.upload_selected_preset"] = new("Upload selected preset", "Ausgewaehltes Preset hochladen", "Envoyer le preset selectionne", "Cargar preset selecziunau"),
        ["button.refresh_presets"] = new("Refresh", "Aktualisieren", "Actualiser", "Actualisar"),
        ["button.load_central_presets"] = new("Load central presets", "Zentrale Presets laden", "Charger les presets centraux", "Cargar presets centrals"),
        ["button.delete_preset"] = new("Delete preset", "Preset loeschen", "Supprimer preset", "Stizzar preset"),
        ["button.save_current_preset"] = new("Save current preset", "Aktuelles Preset speichern", "Enregistrer preset actuel", "Salvar preset actual"),
        ["button.import_selected_central_preset"] = new("Import selected central preset", "Ausgewaehltes zentrales Preset importieren", "Importer le preset central selectionne", "Importar preset central selecziunau"),
        ["checkbox.set_active"] = new("Set active", "Aktiv setzen", "Definir actif", "Definir activ"),
        ["checkbox.install_and_activate"] = new("Install and activate", "Installieren und aktivieren", "Installer et activer", "Installar ed activar"),
        ["checkbox.set_active_on_server"] = new("Set active on server", "Auf Server aktiv setzen", "Definir actif sur le serveur", "Definir activ sil server"),
        ["hint.central_preset_server_usage"] = new("Presets are loaded through the authenticated central server session.", "Presets werden ueber die authentifizierte Central-Server-Sitzung geladen.", "Les presets sont charges via la session authentifiee du serveur central.", "Presets vegnan cargai tras la sessiun autentificada dil server central."),
        ["tooltip.preset_name_example"] = new("Preset name (e.g. Summer Menu)", "Preset-Name (z.B. Sommerkarte)", "Nom du preset (ex. Menu ete)", "Num preset (p.ex. menu stad)"),
        ["tooltip.central_server_url"] = new("Pinned central server URL", "Angeheftete Central-Server-URL", "URL epinglee du serveur central", "URL fixada dil server central"),
        ["tooltip.central_preset_name"] = new("Override preset name", "Preset-Name ueberschreiben", "Remplacer le nom du preset", "Surpassar num preset"),
        ["preset.option_format"] = new("{0} ({1} items)", "{0} ({1} Artikel)", "{0} ({1} articles)", "{0} ({1} artitgels)"),
        ["preset.option_active_format"] = new("{0} ({1} items) - active", "{0} ({1} Artikel) - aktiv", "{0} ({1} articles) - actif", "{0} ({1} artitgels) - activ"),
        ["preset.option_central_format"] = new("{0} ({1} items)", "{0} ({1} Artikel)", "{0} ({1} articles)", "{0} ({1} artitgels)"),
        ["preset.option_central_active_format"] = new("{0} ({1} items) - active on server", "{0} ({1} Artikel) - auf Server aktiv", "{0} ({1} articles) - actif sur le serveur", "{0} ({1} artitgels) - activ sil server"),
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
        ["status.preset_server_url_required"] = new("A central server connection is required.", "Eine Central-Server-Verbindung ist erforderlich.", "Une connexion au serveur central est requise.", "Ina connexiun cul server central ei necessaria."),
        ["status.central_presets_load_failed"] = new("Central presets could not be loaded: {0}", "Zentrale Presets konnten nicht geladen werden: {0}", "Les presets centraux n'ont pas pu etre charges: {0}", "Presets centrals buca cargai: {0}"),
        ["status.central_presets_loaded"] = new("Loaded {0} central presets.", "{0} zentrale Presets geladen.", "{0} presets centraux charges.", "{0} presets centrals cargai."),
        ["status.central_preset_select_required"] = new("Select a central preset first.", "Bitte zuerst ein zentrales Preset waehlen.", "Selectionnez d'abord un preset central.", "Selecziunescha emprema in preset central."),
        ["status.preset_import_failed"] = new("Central preset import failed: {0}", "Import des zentralen Presets fehlgeschlagen: {0}", "Import du preset central echoue: {0}", "Import preset central buca reussiu: {0}"),
        ["status.preset_imported"] = new("Central preset '{0}' imported.", "Zentrales Preset '{0}' importiert.", "Preset central '{0}' importe.", "Preset central '{0}' importaus."),
        ["status.preset_imported_and_switched"] = new("Central preset '{0}' imported and activated locally.", "Zentrales Preset '{0}' importiert und lokal aktiviert.", "Preset central '{0}' importe et active localement.", "Preset central '{0}' importa ed activaus localmein."),
        ["status.preset_upload_failed"] = new("Central preset upload failed: {0}", "Upload zum Central Server fehlgeschlagen: {0}", "Envoi du preset central echoue: {0}", "Upload preset central buca reussiu: {0}"),
        ["status.preset_uploaded"] = new("Central preset '{0}' uploaded.", "Zentrales Preset '{0}' hochgeladen.", "Preset central '{0}' televerse.", "Preset central '{0}' cargaus ensi.")
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

    internal static IReadOnlyList<UiOption<string>> BuildPaymentMethodOptions(UiLanguage language)
    {
        return new[]
        {
            new UiOption<string>("Cash", Get(language, "payment.cash")),
            new UiOption<string>("Card", Get(language, "payment.card")),
            new UiOption<string>("RFID/NFC", Get(language, "payment.rfid_nfc")),
            new UiOption<string>("TWINT", Get(language, "payment.twint")),
            new UiOption<string>("Mobile", Get(language, "payment.mobile"))
        };
    }

    internal static IReadOnlyList<UiOption<CashSlothRole>> BuildRoleOptions(UiLanguage language)
    {
        return new[]
        {
            new UiOption<CashSlothRole>(CashSlothRole.User, Get(language, "role.user")),
            new UiOption<CashSlothRole>(CashSlothRole.Creator, Get(language, "role.creator")),
            new UiOption<CashSlothRole>(CashSlothRole.Admin, Get(language, "role.admin"))
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
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("has_seen_onboarding")] bool HasSeenOnboarding = false);
