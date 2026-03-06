using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;
using WpfButton = System.Windows.Controls.Button;

namespace CashSloth.App;

public partial class MainWindow : Window
{
    private const string AllCategoriesLabel = "All";

    private readonly ObservableCollection<CartLineView> _lines = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<CatalogItemEditor> _catalog = new();
    private readonly List<string> _extraCategories = new();
    private IntPtr _cart = IntPtr.Zero;
    private long _currentGivenCents;
    private bool _coreInitialized;
    private CartSnapshot? _lastSnapshot;
    private CustomerDisplayWindow? _customerDisplayWindow;
    private string? _editingItemId;
    private string _activeCategory = AllCategoriesLabel;

    public MainWindow()
    {
        InitializeComponent();
        CartLinesGrid.ItemsSource = _lines;

        InitializeCatalogDefaults();
        RefreshCategoryControls();
        RenderProductButtons();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Initializing core...";
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
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        CloseCatalogEditor();
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
            StatusText.Text = "Selected product does not exist anymore.";
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
        OpenCatalogEditorButton.Visibility = editModeEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (!editModeEnabled)
        {
            CloseCatalogEditor();
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
            StatusText.Text = "Enable edit mode first.";
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

    private void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var newCategory = NewCategoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newCategory))
        {
            StatusText.Text = "Category name is required.";
            return;
        }

        if (!GetKnownCategories().Any(category => string.Equals(category, newCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _extraCategories.Add(newCategory);
        }

        _activeCategory = newCategory;
        EditCategoryCombo.Text = newCategory;
        NewCategoryTextBox.Text = string.Empty;
        RefreshCategoryControls();
        RenderProductButtons();
        StatusText.Text = $"Category '{newCategory}' added.";
    }

    private void OnSaveProductClick(object sender, RoutedEventArgs e)
    {
        var item = FindCatalogItem(_editingItemId);
        if (item == null)
        {
            StatusText.Text = "Select a product to edit.";
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

        if (!ApplyCatalogUpdate("Product updated. Cart reset."))
        {
            return;
        }

        LoadEditor(item);
        ShowCatalogEditor();
        RenderProductButtons();
    }

    private void OnAddNewProductClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadEditorValues(out var name, out var unitCents, out var category))
        {
            return;
        }

        var previousCategory = _activeCategory;
        var item = new CatalogItemEditor(BuildUniqueProductId(name), name, unitCents, category);
        _catalog.Add(item);
        _activeCategory = category;

        if (!ApplyCatalogUpdate("New product added. Cart reset."))
        {
            _catalog.Remove(item);
            _activeCategory = previousCategory;
            RefreshCategoryControls();
            RenderProductButtons();
            return;
        }

        LoadEditor(item);
        ShowCatalogEditor();
        RenderProductButtons();
    }

    private void OnDeleteProductClick(object sender, RoutedEventArgs e)
    {
        var item = FindCatalogItem(_editingItemId);
        if (item == null)
        {
            StatusText.Text = "Select a product to delete.";
            return;
        }

        if (_catalog.Count <= 1)
        {
            StatusText.Text = "At least one product must remain.";
            return;
        }

        var removedIndex = _catalog.IndexOf(item);
        _catalog.RemoveAt(removedIndex);

        if (!ApplyCatalogUpdate("Product deleted. Cart reset."))
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
            StatusText.Text = "Select a cart line to remove.";
            return;
        }

        if (!TryCoreCall(NativeMethods.cs_cart_remove_line(_cart, index), "remove line"))
        {
            return;
        }

        RefreshFromCoreJson();
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

        if (sender is not WpfButton button || button.Tag is not string centsText || !long.TryParse(centsText, out var cents) || cents <= 0)
        {
            return;
        }

        var nextGiven = _currentGivenCents + cents;
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

        if (!TryParsePriceInChf(CustomGivenTextBox.Text, out var cents) || cents <= 0)
        {
            StatusText.Text = "Custom amount must be a valid CHF value greater than 0.";
            return;
        }

        var nextGiven = _currentGivenCents + cents;
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
                StatusText.Text = "Cart JSON returned empty.";
                return;
            }

            var snapshot = JsonSerializer.Deserialize<CartSnapshot>(json, _jsonOptions);
            if (snapshot == null)
            {
                StatusText.Text = "Unable to read cart JSON.";
                return;
            }

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

            TotalValueText.Text = CurrencyFormatter.FormatCents(snapshot.TotalCents);
            GivenValueText.Text = CurrencyFormatter.FormatCents(snapshot.GivenCents);

            var rawChange = snapshot.ChangeCents;
            var changeCents = rawChange > 0 ? rawChange : Math.Max(snapshot.GivenCents - snapshot.TotalCents, 0);
            var missingCents = Math.Max(snapshot.TotalCents - snapshot.GivenCents, 0);

            ChangeValueText.Text = CurrencyFormatter.FormatCents(changeCents);
            BalanceHintText.Text = missingCents > 0
                ? $"Missing {CurrencyFormatter.FormatCents(missingCents)}"
                : changeCents > 0
                    ? $"Return {CurrencyFormatter.FormatCents(changeCents)}"
                    : "Exact amount";

            _currentGivenCents = snapshot.GivenCents;
            _lastSnapshot = snapshot;
            _customerDisplayWindow?.Update(snapshot);
            StatusText.Text = string.Empty;
        }
        catch (JsonException ex)
        {
            StatusText.Text = $"Failed to parse cart JSON: {ex.Message}";
        }
        finally
        {
            if (jsonPtr != IntPtr.Zero)
            {
                NativeMethods.cs_free(jsonPtr);
            }
        }
    }

    private bool EnsureCart()
    {
        if (_cart == IntPtr.Zero)
        {
            StatusText.Text = "Cart is not ready yet.";
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
        var message = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error.";
        StatusText.Text = $"Failed to {action} ({result}): {message}";
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
        StatusText.Text = successStatus;
        return true;
    }

    private void RenderCategoryButtons(List<string>? categories = null)
    {
        CategoryButtonsPanel.Children.Clear();

        categories ??= GetKnownCategories();

        var allCategories = new List<string> { AllCategoriesLabel };
        allCategories.AddRange(categories);

        foreach (var category in allCategories)
        {
            var button = new WpfButton
            {
                Tag = category,
                Content = category,
                Margin = new Thickness(4),
                Width = 128,
                Height = 48,
                FontSize = 15,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (string.Equals(category, _activeCategory, StringComparison.OrdinalIgnoreCase))
            {
                button.Background = Brushes.LightSteelBlue;
                button.BorderBrush = Brushes.SteelBlue;
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
                Text = "No items in this category.",
                FontSize = 16,
                Margin = new Thickness(8)
            });
            return;
        }

        foreach (var item in filteredCatalog)
        {
            var button = new WpfButton
            {
                Tag = item.Id,
                Margin = new Thickness(6),
                Width = 160,
                Height = 96,
                FontSize = 16,
                Content = $"{item.Name}\n{CurrencyFormatter.FormatCents(item.UnitCents)}",
                ToolTip = item.Category
            };

            if (IsEditModeEnabled() && string.Equals(item.Id, _editingItemId, StringComparison.Ordinal))
            {
                button.Background = Brushes.LightGoldenrodYellow;
            }

            button.Click += OnProductButtonClick;
            ProductsPanel.Children.Add(button);
        }
    }

    private List<CatalogItemEditor> GetFilteredCatalog()
    {
        if (string.IsNullOrWhiteSpace(_activeCategory) || string.Equals(_activeCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
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

        if (!string.Equals(_activeCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase) &&
            !categories.Any(category => string.Equals(category, _activeCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _activeCategory = AllCategoriesLabel;
        }

        EditCategoryCombo.ItemsSource = categories;
        if (string.IsNullOrWhiteSpace(EditCategoryCombo.Text))
        {
            EditCategoryCombo.Text = categories.FirstOrDefault() ?? "General";
        }

        RenderCategoryButtons(categories);
        RefreshEditorList();
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
        EditPriceTextBox.Text = (item.UnitCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
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
            StatusText.Text = "Name is required.";
            return false;
        }

        if (!TryParsePriceInChf(EditPriceTextBox.Text, out unitCents))
        {
            category = string.Empty;
            StatusText.Text = "Price must be a valid CHF amount (e.g. 4.50).";
            return false;
        }

        category = EditCategoryCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "General";
        }

        return true;
    }

    private static bool TryParsePriceInChf(string input, out long cents)
    {
        cents = 0;

        var cleaned = input.Replace("CHF", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ||
            decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            if (value < 0)
            {
                return false;
            }

            cents = (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
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

        _activeCategory = AllCategoriesLabel;
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

