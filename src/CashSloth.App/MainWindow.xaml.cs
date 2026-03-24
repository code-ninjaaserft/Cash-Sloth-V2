using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Screen = System.Windows.Forms.Screen;
using WpfButton = System.Windows.Controls.Button;

namespace CashSloth.App;

public partial class MainWindow : Window
{
    private const string AllCategoriesToken = "__ALL__";
    private const string DefaultPresetSelection = "DEFAULT";
    private const double ToolbarExpandedHeight = 240;
    private const double ToolbarCollapsedHeight = 66;
    private const double CategoryButtonMinWidth = 128;
    private const double CategoryButtonWrapWidth = 176;
    private const double CategoryButtonTextWrapWidth = 156;

    private readonly ObservableCollection<CartLineView> _lines = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<CatalogItemEditor> _catalog = new();
    private readonly List<string> _extraCategories = new();
    private readonly AssortmentPresetStore _assortmentStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly FxRateProvider _fxRateProvider = new();
    private readonly OnlinePresetProvider _onlinePresetProvider = new();
    private readonly AuthSqliteStore _authStore = new();
    private IntPtr _cart = IntPtr.Zero;
    private long _currentGivenCents;
    private bool _coreInitialized;
    private bool _isApplyingSettings;
    private CartSnapshot? _lastSnapshot;
    private CustomerDisplayWindow? _customerDisplayWindow;
    private string? _editingItemId;
    private string? _catalogLoadWarning;
    private AppSettings _settings = AppSettings.Default;
    private string _activeCategory = AllCategoriesToken;
    private string _activePresetId = DefaultPresetSelection;
    private AuthSessionUser? _currentUser;
    private List<AuthAccountSummary> _accountSummaries = new();
    private int _quantityEditLineIndex = -1;
    private int _quantityEditCurrentQty;
    private string _quantityEditItemId = string.Empty;
    private string _quantityEditItemLabel = string.Empty;
    private bool _isToolbarCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        LoadAndApplySettings();

        CartLinesGrid.ItemsSource = _lines;

        InitializeCatalogFromStore();
        RefreshCategoryControls();
        RenderProductButtons();
        RefreshPresetControls();
        InitializeAuthUi();
        ApplyLocalizedLiterals();
        RefreshQuickTenderButtons();
        UpdateSummaryValues(0, 0, 0);
    }

    private void LoadAndApplySettings()
    {
        _settings = _settingsStore.Load();
        _fxRateProvider.TryRefreshRates(out _);
        ApplySettings(save: false);
    }

    private void ApplySettings(bool save)
    {
        var culture = UiLocalizer.GetCulture(_settings.Language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var rateFromChf = _fxRateProvider.GetRateFromChf(_settings.Currency);
        CurrencyFormatter.Configure(_settings.Currency, culture, rateFromChf);
        ApplyTheme();
        RefreshSettingsSelectors();
        ApplyLocalizedLiterals();
        RefreshQuickTenderButtons();

        if (_lastSnapshot != null)
        {
            ApplySnapshot(_lastSnapshot);
        }
        else
        {
            UpdateSummaryValues(0, 0, 0);
            BalanceHintText.Text = L("hint.exact_amount");
        }

        if (_customerDisplayWindow != null)
        {
            _customerDisplayWindow.ApplyLocalization(_settings.Language);
            if (_lastSnapshot != null)
            {
                _customerDisplayWindow.Update(_lastSnapshot);
            }
        }

        if (save && !_settingsStore.TrySave(_settings, out var saveError))
        {
            StatusText.Text = Lf("status.settings_save_failed", saveError ?? string.Empty);
        }
    }

    private void RefreshSettingsSelectors()
    {
        _isApplyingSettings = true;
        try
        {
            LanguageComboBox.ItemsSource = UiLocalizer.BuildLanguageOptions(_settings.Language);
            LanguageComboBox.SelectedValue = _settings.Language;

            CurrencyComboBox.ItemsSource = UiLocalizer.BuildCurrencyOptions(_settings.Language);
            CurrencyComboBox.SelectedValue = _settings.Currency;

            ThemeComboBox.ItemsSource = UiLocalizer.BuildThemeOptions(_settings.Language);
            ThemeComboBox.SelectedValue = _settings.Theme;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || LanguageComboBox.SelectedValue is not UiLanguage language || language == _settings.Language)
        {
            return;
        }

        _settings = _settings with { Language = language };
        ApplySettings(save: true);
    }

    private void OnCurrencySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || CurrencyComboBox.SelectedValue is not UiCurrency currency || currency == _settings.Currency)
        {
            return;
        }

        _fxRateProvider.TryRefreshRates(out _);
        _settings = _settings with { Currency = currency };
        ApplySettings(save: true);
        RenderProductButtons();
        RefreshEditorList();
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings || ThemeComboBox.SelectedValue is not UiThemeMode theme || theme == _settings.Theme)
        {
            return;
        }

        _settings = _settings with { Theme = theme };
        ApplySettings(save: true);
        RenderCategoryButtons();
        RenderProductButtons();
    }

    private void OnToolbarTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabControl tabControl)
        {
            return;
        }

        var clickedTab = FindToolbarTabFromSource(e.OriginalSource, tabControl);
        if (clickedTab == null)
        {
            return;
        }

        if (ReferenceEquals(tabControl.SelectedItem, clickedTab))
        {
            SetToolbarCollapsed(!_isToolbarCollapsed);
            e.Handled = true;
        }
    }

    private void OnToolbarTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ToolbarTabControl))
        {
            return;
        }

        if (_isToolbarCollapsed)
        {
            SetToolbarCollapsed(false);
        }
    }

    private void SetToolbarCollapsed(bool collapsed)
    {
        _isToolbarCollapsed = collapsed;
        ToolbarRowDefinition.Height = new GridLength(collapsed ? ToolbarCollapsedHeight : ToolbarExpandedHeight);
    }

    private static TabItem? FindToolbarTabFromSource(object? originalSource, TabControl tabControl)
    {
        if (originalSource is not DependencyObject source)
        {
            return null;
        }

        DependencyObject? current = source;
        while (current != null)
        {
            if (current is TabItem tabItem &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(tabItem), tabControl))
            {
                return tabItem;
            }

            current = GetVisualOrLogicalParent(current);
        }

        return null;
    }

    private static DependencyObject? GetVisualOrLogicalParent(DependencyObject child)
    {
        if (child is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(child);
        }

        return child switch
        {
            FrameworkElement frameworkElement => frameworkElement.Parent,
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => null
        };
    }

    private void OnRefreshPresetListClick(object sender, RoutedEventArgs e)
    {
        RefreshPresetControls(ResolveSelectedPresetId());
    }

    private void OnSwitchPresetClick(object sender, RoutedEventArgs e)
    {
        var presetId = ResolveSelectedPresetId();
        if (string.IsNullOrWhiteSpace(presetId))
        {
            StatusText.Text = L("status.preset_select_required");
            return;
        }

        if (!_assortmentStore.TryLoadPreset(presetId, out var catalog, out var extraCategories, out var loadError))
        {
            StatusText.Text = Lf("status.preset_switch_failed", loadError ?? string.Empty);
            return;
        }

        if (!_assortmentStore.TrySetActivePreset(presetId, out var setActiveError))
        {
            StatusText.Text = Lf("status.preset_switch_failed", setActiveError ?? string.Empty);
            return;
        }

        _activePresetId = presetId;
        if (!ApplyPresetCatalog(catalog, extraCategories, Lf("status.preset_switched", presetId)))
        {
            return;
        }

        RefreshPresetControls(presetId);
    }

    private void OnSaveCurrentPresetClick(object sender, RoutedEventArgs e)
    {
        var presetName = NewPresetNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            StatusText.Text = L("status.preset_name_required");
            return;
        }

        var presetId = BuildUniquePresetId(presetName);
        var setActive = SetSavedPresetActiveCheckBox.IsChecked == true;

        if (!_assortmentStore.TryUpsertPreset(presetId, presetName, _catalog, _extraCategories, setActive, out var saveError))
        {
            StatusText.Text = Lf("status.preset_save_failed", saveError ?? string.Empty);
            return;
        }

        if (setActive)
        {
            _activePresetId = presetId;
        }

        NewPresetNameTextBox.Text = string.Empty;
        RefreshPresetControls(presetId);
        StatusText.Text = Lf("status.preset_saved", presetName);
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        var presetId = ResolveSelectedPresetId();
        if (string.IsNullOrWhiteSpace(presetId))
        {
            StatusText.Text = L("status.preset_select_required");
            return;
        }

        if (!_assortmentStore.TryDeletePreset(presetId, out var deleteError))
        {
            StatusText.Text = Lf("status.preset_delete_failed", deleteError ?? string.Empty);
            return;
        }

        if (!_assortmentStore.TryLoad(out var catalog, out var extraCategories, out var loadError))
        {
            StatusText.Text = Lf("status.preset_switch_failed", loadError ?? string.Empty);
            return;
        }

        if (!_assortmentStore.TryGetPresetSummaries(out var summaries, out _))
        {
            summaries = new List<AssortmentPresetSummary>();
        }

        _activePresetId = summaries.FirstOrDefault(summary => summary.IsActive)?.Id ?? DefaultPresetSelection;
        if (!ApplyPresetCatalog(catalog, extraCategories, Lf("status.preset_deleted", presetId)))
        {
            return;
        }

        RefreshPresetControls(_activePresetId);
    }

    private void OnImportOnlinePresetClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(UserRole.Downloader, "import online presets"))
        {
            return;
        }

        var url = OnlinePresetUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText.Text = L("status.preset_url_required");
            return;
        }

        if (!_onlinePresetProvider.TryDownloadPreset(url, out var downloadedPreset, out var downloadError) || downloadedPreset == null)
        {
            StatusText.Text = Lf("status.preset_import_failed", downloadError ?? string.Empty);
            return;
        }

        if (!string.IsNullOrWhiteSpace(OnlinePresetNameTextBox.Text))
        {
            downloadedPreset = downloadedPreset with { Name = OnlinePresetNameTextBox.Text.Trim() };
        }

        var setActive = SetImportedPresetActiveCheckBox.IsChecked == true;
        if (!_assortmentStore.TryUpsertPreset(downloadedPreset, setActive, out var persistedPresetId, out var saveError))
        {
            StatusText.Text = Lf("status.preset_import_failed", saveError ?? string.Empty);
            return;
        }

        _activePresetId = persistedPresetId;

        if (setActive)
        {
            if (!_assortmentStore.TryLoadPreset(persistedPresetId, out var catalog, out var extraCategories, out var loadError))
            {
                StatusText.Text = Lf("status.preset_switch_failed", loadError ?? string.Empty);
                return;
            }

            if (!ApplyPresetCatalog(catalog, extraCategories, Lf("status.preset_imported_and_switched", downloadedPreset.Name)))
            {
                return;
            }
        }
        else
        {
            StatusText.Text = Lf("status.preset_imported", downloadedPreset.Name);
        }

        RefreshPresetControls(persistedPresetId);
        OnlinePresetNameTextBox.Text = string.Empty;
    }

    private void OnUploadPresetClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(UserRole.Creator, "upload presets"))
        {
            return;
        }

        var presetId = ResolveSelectedPresetId();
        if (string.IsNullOrWhiteSpace(presetId))
        {
            StatusText.Text = L("status.preset_select_required");
            return;
        }

        StatusText.Text = $"Upload endpoint for preset '{presetId}' is not wired yet. Authentication and role checks are active.";
    }

    private void InitializeAuthUi()
    {
        AccountRoleComboBox.ItemsSource = Enum.GetValues<UserRole>();
        AccountRoleComboBox.SelectedItem = UserRole.Downloader;
        AccountEnabledCheckBox.IsChecked = true;

        if (!_authStore.TryEnsureInitialized(out var seededDefaultAdmin, out var initError))
        {
            StatusText.Text = $"Account store initialization failed: {initError ?? "unknown error"}";
            RefreshAuthUi(loadAccounts: false);
            return;
        }

        var seededMessage = seededDefaultAdmin
            ? "Default admin created: username 'admin', password 'admin'. Change it immediately."
            : null;

        if (_authStore.TryAuthenticateLocalAdminBypass(out var recoveredUser, out var recoveryError) && recoveredUser != null)
        {
            _currentUser = recoveredUser;
            RefreshAuthUi(loadAccounts: true);
            StatusText.Text = seededMessage == null
                ? $"Local admin recovery unlocked this laptop as '{recoveredUser.Username}'."
                : $"{seededMessage} Local admin recovery unlocked this laptop as '{recoveredUser.Username}'.";
            return;
        }

        RefreshAuthUi(loadAccounts: true);

        if (seededDefaultAdmin)
        {
            StatusText.Text = seededMessage;
            return;
        }

        if (!string.IsNullOrWhiteSpace(recoveryError))
        {
            StatusText.Text = $"Local admin recovery unavailable: {recoveryError}";
        }
    }

    private void RefreshAuthUi(bool loadAccounts)
    {
        var isSignedIn = _currentUser != null;
        CurrentUserTextBlock.Text = isSignedIn
            ? $"{_currentUser!.Username} ({_currentUser.Role})"
            : "Not signed in";

        LoginUsernameTextBox.IsEnabled = !isSignedIn;
        LoginPasswordBox.IsEnabled = !isSignedIn;
        LoginButton.IsEnabled = !isSignedIn;
        LogoutButton.IsEnabled = isSignedIn;
        LocalAdminRecoveryButton.IsEnabled = !isSignedIn;

        var canDownload = HasRole(UserRole.Downloader);
        var canUpload = HasRole(UserRole.Creator);
        var canManage = HasRole(UserRole.Admin);

        OnlinePresetUrlTextBox.IsEnabled = canDownload;
        OnlinePresetNameTextBox.IsEnabled = canDownload;
        SetImportedPresetActiveCheckBox.IsEnabled = canDownload;
        ImportOnlinePresetButton.IsEnabled = canDownload;
        UploadPresetButton.IsEnabled = canUpload;

        AccountsListBox.IsEnabled = canManage;
        RefreshAccountsButton.IsEnabled = canManage;
        AccountUsernameTextBox.IsEnabled = canManage;
        AccountPasswordBox.IsEnabled = canManage;
        AccountRoleComboBox.IsEnabled = canManage;
        AccountEnabledCheckBox.IsEnabled = canManage;
        SaveAccountButton.IsEnabled = canManage;
        DeleteAccountButton.IsEnabled = canManage;

        if (canManage && loadAccounts)
        {
            _ = RefreshAccountsFromStore(updateStatusOnError: true);
        }
        else if (!canManage)
        {
            _accountSummaries = new List<AuthAccountSummary>();
            AccountsListBox.ItemsSource = _accountSummaries;
            ClearAccountEditor();
        }
    }

    private void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var username = LoginUsernameTextBox.Text.Trim();
        var password = LoginPasswordBox.Password;

        if (!_authStore.TryAuthenticate(username, password, out var user, out var authError) || user == null)
        {
            StatusText.Text = $"Login failed: {authError ?? "invalid credentials"}";
            return;
        }

        _currentUser = user;
        LoginPasswordBox.Password = string.Empty;
        RefreshAuthUi(loadAccounts: true);
        StatusText.Text = $"Signed in as '{user.Username}' ({user.Role}).";
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null)
        {
            return;
        }

        var previousUser = _currentUser.Username;
        _currentUser = null;
        RefreshAuthUi(loadAccounts: false);
        StatusText.Text = $"Signed out '{previousUser}'.";
    }

    private void OnLocalAdminRecoveryClick(object sender, RoutedEventArgs e)
    {
        if (_currentUser != null)
        {
            return;
        }

        if (!_authStore.TryAuthenticateLocalAdminBypass(out var recoveredUser, out var recoveryError) || recoveredUser == null)
        {
            StatusText.Text = $"Local admin recovery failed: {recoveryError ?? "unknown error"}";
            return;
        }

        _currentUser = recoveredUser;
        RefreshAuthUi(loadAccounts: true);
        StatusText.Text = $"Local admin recovery unlocked this laptop as '{recoveredUser.Username}'.";
    }

    private void OnRefreshAccountsClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(UserRole.Admin, "manage accounts"))
        {
            return;
        }

        if (!RefreshAccountsFromStore(updateStatusOnError: true))
        {
            return;
        }

        StatusText.Text = "Account list refreshed.";
    }

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsListBox.SelectedItem is not AuthAccountSummary selected)
        {
            return;
        }

        AccountUsernameTextBox.Text = selected.Username;
        AccountPasswordBox.Password = string.Empty;
        AccountRoleComboBox.SelectedItem = selected.Role;
        AccountEnabledCheckBox.IsChecked = selected.IsEnabled;
    }

    private void OnSaveAccountClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(UserRole.Admin, "manage accounts"))
        {
            return;
        }

        var username = AccountUsernameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            StatusText.Text = "Account username is required.";
            return;
        }

        if (AccountRoleComboBox.SelectedItem is not UserRole role)
        {
            role = UserRole.Downloader;
        }

        var password = AccountPasswordBox.Password;
        var passwordValue = string.IsNullOrWhiteSpace(password) ? null : password;
        var isEnabled = AccountEnabledCheckBox.IsChecked == true;

        if (!_authStore.TryUpsertAccount(username, passwordValue, role, isEnabled, out var saveError))
        {
            StatusText.Text = $"Account save failed: {saveError ?? "unknown error"}";
            return;
        }

        AccountPasswordBox.Password = string.Empty;
        if (!RefreshAccountsFromStore(updateStatusOnError: true))
        {
            return;
        }

        SyncCurrentUserFromAccountList();
        RefreshAuthUi(loadAccounts: false);
        StatusText.Text = $"Account '{username}' saved.";
    }

    private void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(UserRole.Admin, "manage accounts"))
        {
            return;
        }

        if (AccountsListBox.SelectedItem is not AuthAccountSummary selected)
        {
            StatusText.Text = "Select an account to delete.";
            return;
        }

        if (!_authStore.TryDeleteAccount(selected.Username, out var deleteError))
        {
            StatusText.Text = $"Account delete failed: {deleteError ?? "unknown error"}";
            return;
        }

        if (_currentUser != null &&
            string.Equals(_currentUser.Username, selected.Username, StringComparison.OrdinalIgnoreCase))
        {
            _currentUser = null;
        }

        ClearAccountEditor();
        if (!RefreshAccountsFromStore(updateStatusOnError: true))
        {
            return;
        }

        SyncCurrentUserFromAccountList();
        RefreshAuthUi(loadAccounts: false);
        StatusText.Text = $"Account '{selected.Username}' deleted.";
    }

    private bool RefreshAccountsFromStore(bool updateStatusOnError)
    {
        if (!_authStore.TryListAccounts(out var summaries, out var error))
        {
            if (updateStatusOnError)
            {
                StatusText.Text = $"Failed to load accounts: {error ?? "unknown error"}";
            }

            return false;
        }

        _accountSummaries = summaries;
        AccountsListBox.ItemsSource = _accountSummaries;
        return true;
    }

    private void SyncCurrentUserFromAccountList()
    {
        if (_currentUser == null)
        {
            return;
        }

        var updated = _accountSummaries.FirstOrDefault(summary =>
            string.Equals(summary.Username, _currentUser.Username, StringComparison.OrdinalIgnoreCase));
        if (updated == null || !updated.IsEnabled)
        {
            _currentUser = null;
            return;
        }

        _currentUser = new AuthSessionUser(updated.Username, updated.Role);
    }

    private static void ClearTextBox(TextBox textBox)
    {
        textBox.Text = string.Empty;
    }

    private void ClearAccountEditor()
    {
        AccountsListBox.SelectedItem = null;
        ClearTextBox(AccountUsernameTextBox);
        AccountPasswordBox.Password = string.Empty;
        AccountRoleComboBox.SelectedItem = UserRole.Downloader;
        AccountEnabledCheckBox.IsChecked = true;
    }

    private bool EnsureRole(UserRole minimumRole, string action)
    {
        if (_currentUser == null)
        {
            StatusText.Text = $"Sign in is required to {action}.";
            return false;
        }

        if (!HasRole(minimumRole))
        {
            StatusText.Text = $"Permission denied. '{minimumRole}' role or higher is required to {action}.";
            return false;
        }

        return true;
    }

    private bool HasRole(UserRole minimumRole)
    {
        return _currentUser != null && _currentUser.Role >= minimumRole;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = L("status.initializing_core");
        if (!TryCoreCall(NativeMethods.cs_init(), "initialize core"))
        {
            return;
        }

        _coreInitialized = true;

        if (!TryCoreCall(LoadCatalogIntoCore(), "load catalog"))
        {
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_cart_new(out _cart), "create cart"))
        {
            return;
        }

        RefreshFromCoreJson();

        if (!string.IsNullOrWhiteSpace(_catalogLoadWarning))
        {
            StatusText.Text = _catalogLoadWarning;
        }
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        CloseCatalogEditor();
        CloseAddItemOverlay();
        CloseCategoryManager();
        CloseAddCategoryOverlay();
        CloseCartQuantityOverlay();
        CloseCustomerDisplay();

        if (_cart != IntPtr.Zero)
        {
            TryCoreCall(NativeMethods.cs_cart_free(_cart), "free cart");
            _cart = IntPtr.Zero;
        }

        if (_coreInitialized)
        {
            NativeMethods.cs_shutdown();
            _coreInitialized = false;
        }
    }

    private void OnProductButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string itemId || string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        var catalogItem = FindCatalogItem(itemId);
        if (catalogItem == null)
        {
            StatusText.Text = L("status.selected_product_missing");
            return;
        }

        if (IsEditModeEnabled())
        {
            LoadEditor(catalogItem);
            ShowCatalogEditor();
            RenderProductButtons();
            return;
        }

        if (!EnsureCart())
        {
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_cart_add_item_by_id(_cart, itemId, 1), $"add {itemId}"))
        {
            return;
        }

        RefreshFromCoreJson();
    }

    private void OnCategoryButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string category)
        {
            return;
        }

        _activeCategory = category;
        RenderCategoryButtons();
        RenderProductButtons();

        if (!IsEditModeEnabled())
        {
            return;
        }

        var target = GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (target != null)
        {
            LoadEditor(target);
        }
    }

    private void OnEditModeChanged(object sender, RoutedEventArgs e)
    {
        var editModeEnabled = IsEditModeEnabled();
        var overlayVisibility = editModeEnabled ? Visibility.Visible : Visibility.Collapsed;

        OpenCatalogEditorButton.Visibility = overlayVisibility;
        OpenAddItemButton.Visibility = overlayVisibility;
        OpenCategoryManagerButton.Visibility = overlayVisibility;

        if (!editModeEnabled)
        {
            CloseCatalogEditor();
            CloseAddItemOverlay();
            CloseCategoryManager();
            CloseAddCategoryOverlay();
            RenderCategoryButtons();
            RenderProductButtons();
            return;
        }

        var target = FindCatalogItem(_editingItemId) ?? GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (target != null)
        {
            LoadEditor(target);
        }

        RenderCategoryButtons();
        RenderProductButtons();
    }

    private void OnOpenCatalogEditorClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditModeEnabled())
        {
            StatusText.Text = L("status.enable_edit_mode_first");
            return;
        }

        ShowCatalogEditor();
    }

    private void OnCloseCatalogEditorClick(object sender, RoutedEventArgs e)
    {
        CloseCatalogEditor();
    }

    private void OnEditorItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorItemsListBox.SelectedItem is not ListBoxItem listItem || listItem.Tag is not string itemId)
        {
            return;
        }

        var item = FindCatalogItem(itemId);
        if (item == null)
        {
            return;
        }

        LoadEditor(item);
        RenderProductButtons();
    }

    private void OnOpenAddItemOverlayClick(object sender, RoutedEventArgs e)
    {
        ShowAddItemOverlay(_activeCategory, false);
    }

    private void OnCloseAddItemOverlayClick(object sender, RoutedEventArgs e)
    {
        CloseAddItemOverlay();
    }

    private void OnCreateNewItemClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadNewItemValues(out var name, out var unitCents, out var category))
        {
            return;
        }

        var previousCategory = _activeCategory;
        var item = new CatalogItemEditor(BuildUniqueProductId(name), name, unitCents, category);
        _catalog.Add(item);
        _activeCategory = category;

        if (!ApplyCatalogUpdate(L("status.product_added")))
        {
            _catalog.Remove(item);
            _activeCategory = previousCategory;
            RefreshCategoryControls();
            RenderProductButtons();
            return;
        }

        LoadEditor(item);
        CloseAddItemOverlay();

        if (CatalogEditorOverlay.Visibility == Visibility.Visible)
        {
            ShowCatalogEditor();
        }

        if (CategoryManagerOverlay.Visibility == Visibility.Visible)
        {
            RefreshCategoryManagerRows();
        }
    }

    private void ShowAddItemOverlay(string? preferredCategory = null, bool lockCategory = false)
    {
        if (!IsEditModeEnabled())
        {
            StatusText.Text = L("status.enable_edit_mode_first");
            return;
        }

        var resolvedPreferredCategory = string.Equals(preferredCategory, AllCategoriesToken, StringComparison.OrdinalIgnoreCase)
            ? null
            : preferredCategory;

        var categories = GetKnownCategories();
        var selectedCategory = string.IsNullOrWhiteSpace(resolvedPreferredCategory)
            ? categories.FirstOrDefault() ?? "General"
            : categories.FirstOrDefault(category =>
                  string.Equals(category, resolvedPreferredCategory, StringComparison.OrdinalIgnoreCase))
              ?? resolvedPreferredCategory.Trim();

        AddItemCategoryCombo.ItemsSource = categories;
        AddItemCategoryCombo.SelectedItem = categories.FirstOrDefault(category =>
            string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase));
        AddItemCategoryCombo.Text = selectedCategory;
        AddItemCategoryCombo.IsEnabled = !lockCategory;

        AddItemNameTextBox.Text = string.Empty;
        AddItemPriceTextBox.Text = string.Empty;

        AddItemOverlay.Visibility = Visibility.Visible;
        AddItemNameTextBox.Focus();
        AddItemNameTextBox.SelectAll();
    }

    private void CloseAddItemOverlay()
    {
        AddItemCategoryCombo.IsEnabled = true;
        AddItemOverlay.Visibility = Visibility.Collapsed;
    }
    private void OnOpenCategoryManagerClick(object sender, RoutedEventArgs e)
    {
        ShowCategoryManager();
    }

    private void OnCloseCategoryManagerClick(object sender, RoutedEventArgs e)
    {
        CloseCategoryManager();
    }

    private void ShowCategoryManager()
    {
        if (!IsEditModeEnabled())
        {
            StatusText.Text = L("status.enable_edit_mode_first");
            return;
        }

        RefreshCategoryManagerRows();
        CategoryManagerOverlay.Visibility = Visibility.Visible;
    }

    private void CloseCategoryManager()
    {
        CategoryManagerOverlay.Visibility = Visibility.Collapsed;
        CloseAddCategoryOverlay();
    }

    private void OnOpenAddCategoryOverlayClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditModeEnabled())
        {
            StatusText.Text = L("status.enable_edit_mode_first");
            return;
        }

        AddCategoryNameTextBox.Text = string.Empty;
        AddCategoryOverlay.Visibility = Visibility.Visible;
        AddCategoryNameTextBox.Focus();
    }

    private void OnCloseAddCategoryOverlayClick(object sender, RoutedEventArgs e)
    {
        CloseAddCategoryOverlay();
    }

    private void CloseAddCategoryOverlay()
    {
        AddCategoryOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnCreateCategoryClick(object sender, RoutedEventArgs e)
    {
        var newCategory = AddCategoryNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newCategory))
        {
            StatusText.Text = L("status.category_name_required");
            return;
        }

        if (!GetKnownCategories().Any(category => string.Equals(category, newCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _extraCategories.Add(newCategory);
        }

        _activeCategory = newCategory;
        EditCategoryCombo.Text = newCategory;
        AddItemCategoryCombo.Text = newCategory;

        RefreshCategoryControls();
        RenderProductButtons();
        RefreshCategoryManagerRows();
        CloseAddCategoryOverlay();

        if (!TryPersistAssortment(out var persistenceError))
        {
            StatusText.Text = Lf("status.category_added_saved_failed", newCategory, persistenceError ?? string.Empty);
            return;
        }

        StatusText.Text = Lf("status.category_added", newCategory);
    }

    private void RefreshCategoryManagerRows()
    {
        CategoryManagerRowsPanel.Children.Clear();

        foreach (var category in GetKnownCategories())
        {
            var categoryLabel = category;
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBorder = new Border
            {
                BorderBrush = GetThemeBrush("AppControlBorderBrush", Brushes.Gray),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock
                {
                    Text = categoryLabel,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(labelBorder, 0);
            row.Children.Add(labelBorder);

            var addButton = new WpfButton
            {
                Content = "+",
                Width = 42,
                Height = 42,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 20,
                Tag = category,
                ToolTip = Lf("tooltip.add_item_in_category", categoryLabel)
            };
            addButton.Click += OnCategoryManagerAddItemClick;
            Grid.SetColumn(addButton, 1);
            row.Children.Add(addButton);

            var deleteButton = new WpfButton
            {
                Content = "X",
                Width = 42,
                Height = 42,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 18,
                Tag = category,
                ToolTip = Lf("tooltip.delete_category", categoryLabel)
            };
            deleteButton.Click += OnCategoryManagerDeleteCategoryClick;
            Grid.SetColumn(deleteButton, 2);
            row.Children.Add(deleteButton);

            CategoryManagerRowsPanel.Children.Add(row);
        }
    }

    private void OnCategoryManagerAddItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string category)
        {
            return;
        }

        ShowAddItemOverlay(category, true);
    }

    private void OnCategoryManagerDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string category)
        {
            return;
        }

        if (_catalog.Any(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = Lf("status.category_has_items", category);
            return;
        }

        var removed = _extraCategories.RemoveAll(existing =>
            string.Equals(existing, category, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            StatusText.Text = Lf("status.category_cannot_remove", category);
            return;
        }

        if (string.Equals(_activeCategory, category, StringComparison.OrdinalIgnoreCase))
        {
            _activeCategory = AllCategoriesToken;
        }

        RefreshCategoryControls();
        RenderProductButtons();
        RefreshCategoryManagerRows();

        if (!TryPersistAssortment(out var persistenceError))
        {
            StatusText.Text = Lf("status.category_removed_saved_failed", category, persistenceError ?? string.Empty);
            return;
        }

        StatusText.Text = Lf("status.category_removed", category);
    }
    private void OnSaveProductClick(object sender, RoutedEventArgs e)
    {
        var item = FindCatalogItem(_editingItemId);
        if (item == null)
        {
            StatusText.Text = L("status.select_product_edit");
            return;
        }

        if (!TryReadEditorValues(out var name, out var unitCents, out var category))
        {
            return;
        }

        item.Name = name;
        item.UnitCents = unitCents;
        item.Category = category;
        _activeCategory = category;

        if (!ApplyCatalogUpdate(L("status.product_updated")))
        {
            return;
        }

        LoadEditor(item);
        ShowCatalogEditor();
        RenderProductButtons();
    }

    private void OnAddNewProductClick(object sender, RoutedEventArgs e)
    {
        ShowAddItemOverlay(_activeCategory, false);
    }
    private void OnDeleteProductClick(object sender, RoutedEventArgs e)
    {
        var item = FindCatalogItem(_editingItemId);
        if (item == null)
        {
            StatusText.Text = L("status.select_product_delete");
            return;
        }

        if (_catalog.Count <= 1)
        {
            StatusText.Text = L("status.at_least_one_product");
            return;
        }

        var removedIndex = _catalog.IndexOf(item);
        _catalog.RemoveAt(removedIndex);

        if (!ApplyCatalogUpdate(L("status.product_deleted")))
        {
            _catalog.Insert(removedIndex, item);
            return;
        }

        var fallback = GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (fallback != null)
        {
            LoadEditor(fallback);
        }

        ShowCatalogEditor();
        RenderProductButtons();
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        var index = CartLinesGrid.SelectedIndex;
        if (index < 0)
        {
            StatusText.Text = L("status.select_line_remove");
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_cart_remove_line(_cart, index), "remove line"))
        {
            return;
        }

        RefreshFromCoreJson();
    }

    private void OnCartQuantityClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (!TryGetLineIndexFromSender(sender, out var lineIndex))
        {
            return;
        }

        var lines = _lastSnapshot?.Lines;
        if (lines == null || lineIndex < 0 || lineIndex >= lines.Length)
        {
            StatusText.Text = "Cart lines are outdated. Please try again.";
            return;
        }

        var line = lines[lineIndex];
        var itemId = line.Id?.Trim();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            StatusText.Text = "Selected cart line has no valid item id.";
            return;
        }

        _quantityEditLineIndex = lineIndex;
        _quantityEditCurrentQty = Math.Max(1, line.Qty);
        _quantityEditItemId = itemId;
        _quantityEditItemLabel = line.Name ?? itemId;

        CartLinesGrid.SelectedIndex = lineIndex;
        CartQuantityItemText.Text = $"{_quantityEditItemLabel} (current: {_quantityEditCurrentQty})";
        CartQuantityTextBox.Text = _quantityEditCurrentQty.ToString(CultureInfo.CurrentCulture);
        CartQuantityOverlay.Visibility = Visibility.Visible;
        CartQuantityTextBox.Focus();
        CartQuantityTextBox.SelectAll();
    }

    private void OnApplyCartQuantityClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (!TryParseQuantityInput(CartQuantityTextBox.Text, out var targetQty))
        {
            StatusText.Text = "Quantity must be a positive whole number.";
            return;
        }

        if (!TryResolveQuantityEditLine(out var lineIndex, out var currentQty, out var itemId, out var itemLabel, out var resolveError))
        {
            StatusText.Text = resolveError ?? "Could not resolve cart line for quantity update.";
            return;
        }

        if (targetQty == currentQty)
        {
            CloseCartQuantityOverlay();
            StatusText.Text = $"Quantity for '{itemLabel}' is already {targetQty}.";
            return;
        }

        if (targetQty > currentQty)
        {
            var delta = targetQty - currentQty;
            if (!TryCoreCall(NativeMethods.cs_cart_add_item_by_id(_cart, itemId, delta), "set cart quantity"))
            {
                return;
            }
        }
        else
        {
            if (!TryCoreCall(NativeMethods.cs_cart_remove_line(_cart, lineIndex), "reset cart line quantity"))
            {
                return;
            }

            if (!TryCoreCall(NativeMethods.cs_cart_add_item_by_id(_cart, itemId, targetQty), "set cart quantity"))
            {
                return;
            }
        }

        CloseCartQuantityOverlay();
        RefreshFromCoreJson();
        StatusText.Text = $"Quantity for '{itemLabel}' set to {targetQty}.";
    }

    private void OnCloseCartQuantityOverlayClick(object sender, RoutedEventArgs e)
    {
        CloseCartQuantityOverlay();
    }

    private void CloseCartQuantityOverlay()
    {
        CartQuantityOverlay.Visibility = Visibility.Collapsed;
        _quantityEditLineIndex = -1;
        _quantityEditCurrentQty = 0;
        _quantityEditItemId = string.Empty;
        _quantityEditItemLabel = string.Empty;
        CartQuantityTextBox.Text = string.Empty;
    }

    private bool TryGetLineIndexFromSender(object sender, out int lineIndex)
    {
        lineIndex = -1;

        if (sender is not WpfButton button || button.Tag == null)
        {
            return false;
        }

        if (button.Tag is int tagIndex)
        {
            lineIndex = tagIndex;
            return true;
        }

        if (button.Tag is long longIndex)
        {
            lineIndex = (int)longIndex;
            return true;
        }

        if (button.Tag is string textIndex &&
            int.TryParse(textIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
        {
            lineIndex = parsedIndex;
            return true;
        }

        return false;
    }

    private static bool TryParseQuantityInput(string input, out int quantity)
    {
        quantity = 0;

        var trimmed = input.Trim();
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.CurrentCulture, out quantity) && quantity > 0;
    }

    private bool TryResolveQuantityEditLine(out int lineIndex, out int currentQty, out string itemId, out string itemLabel, out string? error)
    {
        lineIndex = _quantityEditLineIndex;
        currentQty = _quantityEditCurrentQty;
        itemId = _quantityEditItemId;
        itemLabel = _quantityEditItemLabel;
        error = null;

        if (lineIndex < 0 || string.IsNullOrWhiteSpace(itemId))
        {
            error = "No cart line selected for quantity update.";
            return false;
        }

        var lines = _lastSnapshot?.Lines;
        if (lines == null || lines.Length == 0)
        {
            error = "Cart is empty.";
            return false;
        }

        var liveIndex = lineIndex;
        if (liveIndex >= lines.Length ||
            !string.Equals(lines[liveIndex].Id, itemId, StringComparison.OrdinalIgnoreCase))
        {
            var targetItemId = itemId;
            liveIndex = Array.FindIndex(lines, line =>
                !string.IsNullOrWhiteSpace(line.Id) &&
                string.Equals(line.Id, targetItemId, StringComparison.OrdinalIgnoreCase));
        }

        if (liveIndex < 0)
        {
            error = "Selected cart line no longer exists.";
            return false;
        }

        var liveLine = lines[liveIndex];
        var liveId = liveLine.Id?.Trim();
        if (string.IsNullOrWhiteSpace(liveId))
        {
            error = "Selected cart line has no valid item id.";
            return false;
        }

        lineIndex = liveIndex;
        currentQty = Math.Max(1, liveLine.Qty);
        itemId = liveId;
        itemLabel = liveLine.Name ?? liveId;
        return true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_cart_clear(_cart), "clear cart"))
        {
            return;
        }

        RefreshFromCoreJson();
    }

    private void OnAddGivenClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (sender is not WpfButton button || button.Tag is not string centsText || !long.TryParse(centsText, out var displayCents) || displayCents <= 0)
        {
            return;
        }

        var baseCents = CurrencyFormatter.ConvertDisplayCentsToBaseCents(displayCents);
        if (baseCents <= 0)
        {
            return;
        }

        var nextGiven = _currentGivenCents + baseCents;
        if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, nextGiven), "set given"))
        {
            return;
        }

        RefreshFromCoreJson();
    }

    private void OnAddCustomGivenClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (!TryParsePrice(CustomGivenTextBox.Text, out var baseCents) || baseCents <= 0)
        {
            StatusText.Text = L("status.custom_amount_invalid");
            return;
        }

        var nextGiven = _currentGivenCents + baseCents;
        if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, nextGiven), "set custom given"))
        {
            return;
        }

        CustomGivenTextBox.Text = string.Empty;
        RefreshFromCoreJson();
    }

    private void OnResetGivenClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, 0), "reset given"))
        {
            return;
        }

        CustomGivenTextBox.Text = string.Empty;
        RefreshFromCoreJson();
    }

    private void RefreshFromCoreJson()
    {
        if (!EnsureCart())
        {
            return;
        }

        IntPtr jsonPtr = IntPtr.Zero;
        try
        {
            var result = NativeMethods.cs_cart_get_lines_json(_cart, out jsonPtr);
            if (!TryCoreCall(result, "get cart JSON"))
            {
                return;
            }

            var json = Marshal.PtrToStringUTF8(jsonPtr);
            if (string.IsNullOrWhiteSpace(json))
            {
                StatusText.Text = L("status.cart_json_empty");
                return;
            }

            var snapshot = JsonSerializer.Deserialize<CartSnapshot>(json, _jsonOptions);
            if (snapshot == null)
            {
                StatusText.Text = L("status.cart_json_unreadable");
                return;
            }

            ApplySnapshot(snapshot);
            StatusText.Text = string.Empty;
        }
        catch (JsonException ex)
        {
            StatusText.Text = Lf("status.failed_parse_cart_json", ex.Message);
        }
        finally
        {
            if (jsonPtr != IntPtr.Zero)
            {
                NativeMethods.cs_free(jsonPtr);
            }
        }
    }

    private void ApplySnapshot(CartSnapshot snapshot)
    {
        _lines.Clear();
        if (snapshot.Lines != null)
        {
            foreach (var line in snapshot.Lines)
            {
                _lines.Add(new CartLineView(
                    line.Name ?? string.Empty,
                    line.Qty,
                    CurrencyFormatter.FormatCents(line.LineTotalCents)));
            }
        }

        if (CartQuantityOverlay.Visibility == Visibility.Visible &&
            (snapshot.Lines == null || snapshot.Lines.Length == 0))
        {
            CloseCartQuantityOverlay();
        }

        var rawChange = snapshot.ChangeCents;
        var changeCents = rawChange > 0 ? rawChange : Math.Max(snapshot.GivenCents - snapshot.TotalCents, 0);
        var missingCents = Math.Max(snapshot.TotalCents - snapshot.GivenCents, 0);

        UpdateSummaryValues(snapshot.TotalCents, snapshot.GivenCents, changeCents);
        BalanceHintText.Text = missingCents > 0
            ? Lf("hint.missing_format", CurrencyFormatter.FormatCents(missingCents))
            : changeCents > 0
                ? Lf("hint.return_format", CurrencyFormatter.FormatCents(changeCents))
                : L("hint.exact_amount");

        _currentGivenCents = snapshot.GivenCents;
        _lastSnapshot = snapshot;
        _customerDisplayWindow?.Update(snapshot);
    }

    private void UpdateSummaryValues(long totalCents, long givenCents, long changeCents)
    {
        TotalValueText.Text = CurrencyFormatter.FormatCents(totalCents);
        GivenValueText.Text = CurrencyFormatter.FormatCents(givenCents);
        ChangeValueText.Text = CurrencyFormatter.FormatCents(changeCents);
    }

    private void RefreshQuickTenderButtons()
    {
        foreach (var child in GivenButtonsPanel.Children)
        {
            if (child is not WpfButton button || button.Tag is not string centsText || !long.TryParse(centsText, out var cents) || cents <= 0)
            {
                continue;
            }

            button.Content = CurrencyFormatter.FormatQuickTenderLabel(cents);
        }
    }

    private bool EnsureCart()
    {
        if (_cart == IntPtr.Zero)
        {
            StatusText.Text = L("status.cart_not_ready");
            return false;
        }

        return true;
    }

    private bool TryCoreCall(int result, string action)
    {
        if (result == 0)
        {
            return true;
        }

        var errorPtr = NativeMethods.cs_last_error();
        var message = Marshal.PtrToStringUTF8(errorPtr) ?? L("status.unknown_error");
        StatusText.Text = Lf("status.failed_action", action, result, message);
        return false;
    }

    private int LoadCatalogIntoCore()
    {
        var payload = new CatalogCorePayload(_catalog
            .Select(item => new CatalogCoreItem(item.Id, item.Name, item.UnitCents))
            .ToArray());

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        return NativeMethods.cs_catalog_load_json(json);
    }

    private bool ApplyCatalogUpdate(string successStatus)
    {
        if (!TryCoreCall(LoadCatalogIntoCore(), "load catalog"))
        {
            return false;
        }

        if (EnsureCart())
        {
            if (!TryCoreCall(NativeMethods.cs_cart_clear(_cart), "clear cart"))
            {
                return false;
            }

            if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, 0), "reset given"))
            {
                return false;
            }

            RefreshFromCoreJson();
        }

        RefreshCategoryControls();
        RenderProductButtons();

        if (!TryPersistAssortment(out var persistenceError))
        {
            StatusText.Text = Lf("status.assortment_not_saved", successStatus, persistenceError ?? string.Empty);
            return true;
        }

        StatusText.Text = successStatus;
        return true;
    }

    private bool TryPersistAssortment(out string? error)
    {
        return _assortmentStore.TrySave(_catalog, _extraCategories, out error);
    }

    private void RenderCategoryButtons(List<string>? categories = null)
    {
        CategoryButtonsPanel.Children.Clear();

        categories ??= GetKnownCategories();

        var allCategories = new List<string> { AllCategoriesToken };
        allCategories.AddRange(categories);

        foreach (var category in allCategories)
        {
            var categoryLabel = string.Equals(category, AllCategoriesToken, StringComparison.Ordinal)
                ? L("category.all")
                : category;

            var button = new WpfButton
            {
                Tag = category,
                Content = new TextBlock
                {
                    Text = categoryLabel,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Width = CategoryButtonTextWrapWidth,
                    LineHeight = 16,
                    MaxHeight = 32,
                    TextAlignment = TextAlignment.Center
                },
                Margin = new Thickness(4),
                MinWidth = CategoryButtonMinWidth,
                MaxWidth = CategoryButtonWrapWidth,
                MinHeight = 48,
                FontSize = 15,
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (string.Equals(category, _activeCategory, StringComparison.OrdinalIgnoreCase))
            {
                button.Background = GetThemeBrush("AppCategorySelectionBrush", Brushes.LightSteelBlue);
                button.BorderBrush = GetThemeBrush("AppCategorySelectionBorderBrush", Brushes.SteelBlue);
                button.FontWeight = FontWeights.SemiBold;
            }

            button.Click += OnCategoryButtonClick;
            CategoryButtonsPanel.Children.Add(button);
        }
    }

    private void RenderProductButtons()
    {
        ProductsPanel.Children.Clear();

        var filteredCatalog = GetFilteredCatalog();
        if (filteredCatalog.Count == 0)
        {
            ProductsPanel.Children.Add(new TextBlock
            {
                Text = L("text.no_items_in_category"),
                FontSize = 16,
                Margin = new Thickness(8)
            });
            return;
        }

        foreach (var item in filteredCatalog)
        {
            var itemName = item.Name;
            var itemCategory = item.Category;
            var button = new WpfButton
            {
                Tag = item.Id,
                Margin = new Thickness(6),
                Width = 160,
                Height = 96,
                FontSize = 16,
                Content = $"{itemName}\n{CurrencyFormatter.FormatCents(item.UnitCents)}",
                ToolTip = itemCategory
            };

            if (IsEditModeEnabled() && string.Equals(item.Id, _editingItemId, StringComparison.Ordinal))
            {
                button.Background = GetThemeBrush("AppEditSelectionBrush", Brushes.LightGoldenrodYellow);
            }

            button.Click += OnProductButtonClick;
            ProductsPanel.Children.Add(button);
        }
    }

    private List<CatalogItemEditor> GetFilteredCatalog()
    {
        if (string.IsNullOrWhiteSpace(_activeCategory) || string.Equals(_activeCategory, AllCategoriesToken, StringComparison.OrdinalIgnoreCase))
        {
            return _catalog;
        }

        return _catalog
            .Where(item => string.Equals(item.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private List<string> GetKnownCategories()
    {
        return _catalog
            .Select(item => item.Category.Trim())
            .Concat(_extraCategories.Select(category => category.Trim()))
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category)
            .ToList();
    }

    private void RefreshCategoryControls()
    {
        var categories = GetKnownCategories();

        if (!string.Equals(_activeCategory, AllCategoriesToken, StringComparison.OrdinalIgnoreCase) &&
            !categories.Any(category => string.Equals(category, _activeCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _activeCategory = AllCategoriesToken;
        }

        EditCategoryCombo.ItemsSource = categories;
        if (string.IsNullOrWhiteSpace(EditCategoryCombo.Text))
        {
            EditCategoryCombo.Text = categories.FirstOrDefault() ?? "General";
        }

        AddItemCategoryCombo.ItemsSource = categories;
        if (string.IsNullOrWhiteSpace(AddItemCategoryCombo.Text))
        {
            AddItemCategoryCombo.Text = categories.FirstOrDefault() ?? "General";
        }

        RenderCategoryButtons(categories);
        RefreshEditorList();

        if (CategoryManagerOverlay.Visibility == Visibility.Visible)
        {
            RefreshCategoryManagerRows();
        }
    }

    private void RefreshEditorList()
    {
        EditorItemsListBox.Items.Clear();

        foreach (var item in _catalog
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            EditorItemsListBox.Items.Add(new ListBoxItem
            {
                Tag = item.Id,
                Content = $"{item.Name} ({item.Category}) - {CurrencyFormatter.FormatCents(item.UnitCents)}",
                Padding = new Thickness(6)
            });
        }

        SelectEditorItem(_editingItemId);
    }

    private void SelectEditorItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        foreach (var entry in EditorItemsListBox.Items)
        {
            if (entry is not ListBoxItem item || item.Tag is not string currentId)
            {
                continue;
            }

            if (!string.Equals(currentId, itemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ReferenceEquals(EditorItemsListBox.SelectedItem, item))
            {
                EditorItemsListBox.SelectedItem = item;
            }

            item.BringIntoView();
            break;
        }
    }

    private void LoadEditor(CatalogItemEditor item)
    {
        _editingItemId = item.Id;
        EditIdText.Text = item.Id;
        EditNameTextBox.Text = item.Name;
        var displayCents = CurrencyFormatter.ConvertBaseCentsToDisplayCents(item.UnitCents);
        EditPriceTextBox.Text = (displayCents / 100m).ToString("0.00", CultureInfo.CurrentCulture);
        EditCategoryCombo.Text = item.Category;
        SelectEditorItem(item.Id);
    }

    private void ShowCatalogEditor()
    {
        if (!IsEditModeEnabled())
        {
            return;
        }

        var target = FindCatalogItem(_editingItemId) ?? GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (target != null)
        {
            LoadEditor(target);
        }

        RefreshEditorList();
        CatalogEditorOverlay.Visibility = Visibility.Visible;
    }

    private void CloseCatalogEditor()
    {
        CatalogEditorOverlay.Visibility = Visibility.Collapsed;
    }

    private CatalogItemEditor? FindCatalogItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        return _catalog.FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryReadEditorValues(out string name, out long unitCents, out string category)
    {
        name = EditNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            unitCents = 0;
            category = string.Empty;
            StatusText.Text = L("status.name_required");
            return false;
        }

        if (!TryParsePrice(EditPriceTextBox.Text, out unitCents))
        {
            category = string.Empty;
            StatusText.Text = L("status.price_invalid");
            return false;
        }

        category = EditCategoryCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "General";
        }

        return true;
    }

    private bool TryReadNewItemValues(out string name, out long unitCents, out string category)
    {
        name = AddItemNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            unitCents = 0;
            category = string.Empty;
            StatusText.Text = L("status.name_required");
            return false;
        }

        if (!TryParsePrice(AddItemPriceTextBox.Text, out unitCents))
        {
            category = string.Empty;
            StatusText.Text = L("status.price_invalid");
            return false;
        }

        category = AddItemCategoryCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "General";
        }

        return true;
    }

    private static bool TryParsePrice(string input, out long baseCents)
    {
        baseCents = 0;

        var cleaned = CurrencyFormatter.StripKnownCurrencyTokens(input);
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ||
            decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            if (value < 0)
            {
                return false;
            }

            var displayCents = (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
            baseCents = CurrencyFormatter.ConvertDisplayCentsToBaseCents(displayCents);
            return true;
        }

        return false;
    }

    private string BuildUniqueProductId(string name)
    {
        var stem = Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "ITEM";
        }

        var candidate = stem;
        var index = 2;
        while (_catalog.Any(item => string.Equals(item.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{stem}_{index}";
            index++;
        }

        return candidate;
    }

    private string BuildUniquePresetId(string name)
    {
        var stem = AssortmentPresetStore.NormalizePresetId(name);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "PRESET";
        }

        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_assortmentStore.TryGetPresetSummaries(out var summaries, out _))
        {
            foreach (var summary in summaries)
            {
                knownIds.Add(summary.Id);
            }
        }

        var candidate = stem;
        var index = 2;
        while (knownIds.Contains(candidate))
        {
            candidate = $"{stem}_{index}";
            index++;
        }

        return candidate;
    }

    private string? ResolveSelectedPresetId()
    {
        if (LocalPresetComboBox.SelectedValue is not string selected || string.IsNullOrWhiteSpace(selected))
        {
            return null;
        }

        return AssortmentPresetStore.NormalizePresetId(selected);
    }

    private void RefreshPresetControls(string? preferredPresetId = null)
    {
        if (!_assortmentStore.TryGetPresetSummaries(out var summaries, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                StatusText.Text = Lf("status.presets_load_failed", error);
            }

            return;
        }

        if (summaries.Count == 0)
        {
            LocalPresetComboBox.ItemsSource = Array.Empty<UiOption<string>>();
            return;
        }

        _activePresetId = summaries.FirstOrDefault(summary => summary.IsActive)?.Id ?? summaries[0].Id;
        var normalizedPreferred = string.IsNullOrWhiteSpace(preferredPresetId)
            ? _activePresetId
            : AssortmentPresetStore.NormalizePresetId(preferredPresetId);

        var options = summaries
            .Select(summary => new UiOption<string>(summary.Id, BuildPresetOptionLabel(summary)))
            .ToArray();

        LocalPresetComboBox.ItemsSource = options;
        LocalPresetComboBox.SelectedValue = options.Any(option => string.Equals(option.Value, normalizedPreferred, StringComparison.OrdinalIgnoreCase))
            ? normalizedPreferred
            : _activePresetId;
    }

    private string BuildPresetOptionLabel(AssortmentPresetSummary summary)
    {
        return summary.IsActive
            ? Lf("preset.option_active_format", summary.Name, summary.ItemCount)
            : Lf("preset.option_format", summary.Name, summary.ItemCount);
    }

    private bool ApplyPresetCatalog(IReadOnlyCollection<CatalogItemEditor> catalog, IReadOnlyCollection<string> extraCategories, string successStatus)
    {
        _catalog.Clear();
        _catalog.AddRange(catalog.Select(item => new CatalogItemEditor(item.Id, item.Name, item.UnitCents, item.Category)));

        _extraCategories.Clear();
        _extraCategories.AddRange(extraCategories.Select(category => category.Trim()).Where(category => !string.IsNullOrWhiteSpace(category)));

        _activeCategory = AllCategoriesToken;
        _editingItemId = _catalog.FirstOrDefault()?.Id;

        RefreshCategoryControls();
        RenderProductButtons();
        RefreshEditorList();

        if (_coreInitialized)
        {
            if (!TryCoreCall(LoadCatalogIntoCore(), "load preset catalog"))
            {
                return false;
            }

            if (EnsureCart())
            {
                if (!TryCoreCall(NativeMethods.cs_cart_clear(_cart), "clear cart"))
                {
                    return false;
                }

                if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, 0), "reset given"))
                {
                    return false;
                }

                RefreshFromCoreJson();
            }
        }
        else
        {
            _lines.Clear();
            _lastSnapshot = null;
            _currentGivenCents = 0;
            UpdateSummaryValues(0, 0, 0);
            BalanceHintText.Text = L("hint.exact_amount");
        }

        StatusText.Text = successStatus;
        return true;
    }

    private void ApplyLocalizedLiterals()
    {
        Title = L("main.title");
        TranslateLiterals(this);
        RefreshPresetControls(_activePresetId);
        RenderCategoryButtons();
        RefreshEditorList();
        RenderProductButtons();
    }

    private void TranslateLiterals(DependencyObject root)
    {
        if (root is HeaderedContentControl headeredControl && headeredControl.Header is string header)
        {
            headeredControl.Header = UiLocalizer.TranslateLiteral(_settings.Language, header);
        }

        if (root is ContentControl contentControl && contentControl.Content is string content)
        {
            contentControl.Content = UiLocalizer.TranslateLiteral(_settings.Language, content);
        }

        if (root is TextBlock textBlock && textBlock != StatusText && textBlock != TotalValueText && textBlock != GivenValueText && textBlock != ChangeValueText)
        {
            textBlock.Text = UiLocalizer.TranslateLiteral(_settings.Language, textBlock.Text);
        }

        if (root is FrameworkElement frameworkElement && frameworkElement.ToolTip is string toolTipText)
        {
            frameworkElement.ToolTip = UiLocalizer.TranslateLiteral(_settings.Language, toolTipText);
        }

        if (root is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
            {
                if (column.Header is string columnHeader)
                {
                    column.Header = UiLocalizer.TranslateLiteral(_settings.Language, columnHeader);
                }
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                TranslateLiterals(dependencyObject);
            }
        }
    }

    private void ApplyTheme()
    {
        var palette = ThemePaletteResolver.Resolve(_settings.Theme);
        SetThemeBrush("AppWindowBackgroundBrush", palette.WindowBackground);
        SetThemeBrush("AppSurfaceBrush", palette.SurfaceBackground);
        SetThemeBrush("AppControlBackgroundBrush", palette.ControlBackground);
        SetThemeBrush("AppControlBorderBrush", palette.ControlBorder);
        SetThemeBrush("AppForegroundBrush", palette.Foreground);
        SetThemeBrush("AppSelectionBackgroundBrush", palette.SelectionBackground);
        SetThemeBrush("AppSelectionForegroundBrush", palette.SelectionForeground);
        SetThemeBrush("AppCategorySelectionBrush", palette.CategorySelectionBackground);
        SetThemeBrush("AppCategorySelectionBorderBrush", palette.CategorySelectionBorder);
        SetThemeBrush("AppEditSelectionBrush", palette.EditSelectionBackground);
        ApplySystemBrushes(palette);
    }

    private static void SetThemeBrush(string key, Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        var replacement = new SolidColorBrush(color);
        Application.Current.Resources[key] = replacement;
    }

    private static Brush GetThemeBrush(string key, Brush fallback)
    {
        return Application.Current.Resources[key] as Brush ?? fallback;
    }

    private static void ApplySystemBrushes(ThemePalette palette)
    {
        SetSystemBrush(SystemColors.WindowBrushKey, palette.SurfaceBackground);
        SetSystemBrush(SystemColors.ControlBrushKey, palette.ControlBackground);
        SetSystemBrush(SystemColors.ControlLightBrushKey, palette.SurfaceBackground);
        SetSystemBrush(SystemColors.ControlDarkBrushKey, palette.ControlBorder);
        SetSystemBrush(SystemColors.ControlTextBrushKey, palette.Foreground);
        SetSystemBrush(SystemColors.WindowTextBrushKey, palette.Foreground);
        SetSystemBrush(SystemColors.HighlightBrushKey, palette.SelectionBackground);
        SetSystemBrush(SystemColors.HighlightTextBrushKey, palette.SelectionForeground);
    }

    private static void SetSystemBrush(ResourceKey key, Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private string L(string key)
    {
        return UiLocalizer.Get(_settings.Language, key);
    }

    private string Lf(string key, params object[] args)
    {
        return UiLocalizer.Format(_settings.Language, key, args);
    }

    private void InitializeCatalogFromStore()
    {
        if (_assortmentStore.TryLoad(out var storedItems, out var storedExtraCategories, out var loadError))
        {
            _catalog.Clear();
            _catalog.AddRange(storedItems);
            _extraCategories.Clear();
            _extraCategories.AddRange(storedExtraCategories);
            _activeCategory = AllCategoriesToken;
            _catalogLoadWarning = null;
            return;
        }

        InitializeCatalogDefaults();

        if (!string.IsNullOrWhiteSpace(loadError))
        {
            _catalogLoadWarning = Lf("status.using_default_assortment", _assortmentStore.FilePath, loadError);
            return;
        }

        if (_assortmentStore.TrySave(_catalog, _extraCategories, out var saveError))
        {
            _catalogLoadWarning = Lf("status.created_assortment_backend", _assortmentStore.FilePath);
            return;
        }

        _catalogLoadWarning = Lf("status.failed_to_create_assortment", _assortmentStore.FilePath, saveError ?? string.Empty);
    }

    private void InitializeCatalogDefaults()
    {
        _catalog.Clear();
        _extraCategories.Clear();

        _catalog.Add(new CatalogItemEditor("COFFEE", "Coffee", 500, "Hot Drinks"));
        _catalog.Add(new CatalogItemEditor("TEA", "Tea", 400, "Hot Drinks"));
        _catalog.Add(new CatalogItemEditor("WATER", "Water", 200, "Soft Drinks"));
        _catalog.Add(new CatalogItemEditor("COLA", "Cola", 350, "Soft Drinks"));
        _catalog.Add(new CatalogItemEditor("CHIPS", "Chips", 250, "Snacks"));
        _catalog.Add(new CatalogItemEditor("CAKE", "Cake", 450, "Snacks"));

        _activeCategory = AllCategoriesToken;
    }

    private bool IsEditModeEnabled()
    {
        return EditModeCheckBox.IsChecked == true;
    }

    private void OnOpenCustomerDisplayClick(object sender, RoutedEventArgs e)
    {
        if (_customerDisplayWindow is { IsVisible: true })
        {
            if (_customerDisplayWindow.WindowState == WindowState.Minimized)
            {
                _customerDisplayWindow.WindowState = WindowState.Normal;
            }

            _customerDisplayWindow.Activate();
            return;
        }

        _customerDisplayWindow = new CustomerDisplayWindow();
        _customerDisplayWindow.ApplyLocalization(_settings.Language);
        _customerDisplayWindow.Closed += (_, _) => _customerDisplayWindow = null;

        PositionCustomerDisplayWindow(_customerDisplayWindow);
        _customerDisplayWindow.Show();

        if (_lastSnapshot != null)
        {
            _customerDisplayWindow.Update(_lastSnapshot);
        }
    }

    private void OnCloseCustomerDisplayClick(object sender, RoutedEventArgs e)
    {
        CloseCustomerDisplay();
    }

    private void CloseCustomerDisplay()
    {
        if (_customerDisplayWindow == null)
        {
            return;
        }

        var window = _customerDisplayWindow;
        _customerDisplayWindow = null;
        window.Close();
    }

    private void PositionCustomerDisplayWindow(Window window)
    {
        var screens = Screen.AllScreens;
        var targetScreen = screens.FirstOrDefault(screen => !screen.Primary);
        if (targetScreen == null)
        {
            return;
        }

        var workingArea = targetScreen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = workingArea.Left / dpi.DpiScaleX;
        window.Top = workingArea.Top / dpi.DpiScaleY;
        window.Width = workingArea.Width / dpi.DpiScaleX;
        window.Height = workingArea.Height / dpi.DpiScaleY;
        window.WindowState = WindowState.Maximized;
    }
}



