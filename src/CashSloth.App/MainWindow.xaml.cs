using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Screen = System.Windows.Forms.Screen;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;

namespace CashSloth.App;

public partial class MainWindow : Window
{
    private const string AllCategoriesLabel = "All";

    private readonly ObservableCollection<CartLineView> _lines = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<CatalogItemEditor> _catalog = new();
    private IntPtr _cart = IntPtr.Zero;
    private long _currentGivenCents;
    private bool _coreInitialized;
    private CartSnapshot? _lastSnapshot;
    private CustomerDisplayWindow? _customerDisplayWindow;
    private string? _editingItemId;

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

    private void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderProductButtons();

        if (!IsEditModeEnabled())
        {
            return;
        }

        if (FindCatalogItem(_editingItemId) is { } current && GetFilteredCatalog().Any(i => i.Id == current.Id))
        {
            return;
        }

        var fallback = GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (fallback != null)
        {
            LoadEditor(fallback);
            RenderProductButtons();
        }
    }

    private void OnEditModeChanged(object sender, RoutedEventArgs e)
    {
        EditPanel.Visibility = IsEditModeEnabled() ? Visibility.Visible : Visibility.Collapsed;

        if (IsEditModeEnabled())
        {
            var target = FindCatalogItem(_editingItemId) ?? GetFilteredCatalog().FirstOrDefault() ?? _catalog.FirstOrDefault();
            if (target != null)
            {
                LoadEditor(target);
            }
        }

        RenderProductButtons();
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

        if (!ApplyCatalogUpdate("Product updated. Cart reset."))
        {
            return;
        }

        LoadEditor(item);
        RenderProductButtons();
    }

    private void OnAddNewProductClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadEditorValues(out var name, out var unitCents, out var category))
        {
            return;
        }

        var item = new CatalogItemEditor(BuildUniqueProductId(name), name, unitCents, category);
        _catalog.Add(item);

        if (!ApplyCatalogUpdate("New product added. Cart reset."))
        {
            _catalog.Remove(item);
            return;
        }

        CategoryFilterCombo.SelectedItem = category;
        LoadEditor(item);
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

        if (sender is WpfButton button && button.Tag is string centsText && long.TryParse(centsText, out var cents))
        {
            var nextGiven = _currentGivenCents + cents;
            if (!TryCoreCall(NativeMethods.cs_payment_set_given_cents(_cart, nextGiven), "set given"))
            {
                return;
            }

            RefreshFromCoreJson();
        }
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
            ChangeValueText.Text = CurrencyFormatter.FormatCents(snapshot.ChangeCents);
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

    private void RenderProductButtons()
    {
        ProductsPanel.Children.Clear();

        foreach (var item in GetFilteredCatalog())
        {
            var button = new WpfButton
            {
                Tag = item.Id,
                Margin = new Thickness(4),
                Width = 108,
                Height = 52,
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
        var selectedCategory = CategoryFilterCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedCategory) || string.Equals(selectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
        {
            return _catalog;
        }

        return _catalog
            .Where(item => string.Equals(item.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RefreshCategoryControls()
    {
        var previousFilter = CategoryFilterCombo.SelectedItem as string;
        var categories = _catalog
            .Select(item => item.Category.Trim())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category)
            .ToList();

        var filterItems = new List<string> { AllCategoriesLabel };
        filterItems.AddRange(categories);

        CategoryFilterCombo.ItemsSource = filterItems;

        var keepFilter = filterItems.Any(item => string.Equals(item, previousFilter, StringComparison.OrdinalIgnoreCase));
        CategoryFilterCombo.SelectedItem = keepFilter ? filterItems.First(item => string.Equals(item, previousFilter, StringComparison.OrdinalIgnoreCase)) : AllCategoriesLabel;

        EditCategoryCombo.ItemsSource = categories;
    }

    private void LoadEditor(CatalogItemEditor item)
    {
        _editingItemId = item.Id;
        EditIdText.Text = item.Id;
        EditNameTextBox.Text = item.Name;
        EditPriceTextBox.Text = (item.UnitCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        EditCategoryCombo.Text = item.Category;
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
        _catalog.Add(new CatalogItemEditor("COFFEE", "Coffee", 500, "Hot Drinks"));
        _catalog.Add(new CatalogItemEditor("TEA", "Tea", 400, "Hot Drinks"));
        _catalog.Add(new CatalogItemEditor("WATER", "Water", 200, "Soft Drinks"));
        _catalog.Add(new CatalogItemEditor("COLA", "Cola", 350, "Soft Drinks"));
        _catalog.Add(new CatalogItemEditor("CHIPS", "Chips", 250, "Snacks"));
        _catalog.Add(new CatalogItemEditor("CAKE", "Cake", 450, "Snacks"));
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
