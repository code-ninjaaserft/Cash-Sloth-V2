using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace CashSloth.App;

public partial class MainWindow : Window
{
    private const string DemoCatalogJson = """
        {"items":[
          {"id":"COFFEE","name":"Coffee","unit_cents":500},
          {"id":"TEA","name":"Tea","unit_cents":400},
          {"id":"WATER","name":"Water","unit_cents":200},
          {"id":"COLA","name":"Cola","unit_cents":350},
          {"id":"CHIPS","name":"Chips","unit_cents":250},
          {"id":"CAKE","name":"Cake","unit_cents":450}
        ]}
        """;

    private readonly ObservableCollection<CartLineView> _lines = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private IntPtr _cart = IntPtr.Zero;
    private long _currentGivenCents;
    private bool _coreInitialized;

    public MainWindow()
    {
        InitializeComponent();
        CartLinesGrid.ItemsSource = _lines;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Initializing core...";
        if (!TryCoreCall(NativeMethods.cs_init(), "initialize core"))
        {
            return;
        }

        _coreInitialized = true;

        if (!TryCoreCall(NativeMethods.cs_catalog_load_json(DemoCatalogJson), "load demo catalog"))
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

    private void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureCart())
        {
            return;
        }

        if (sender is Button button && button.Tag is string itemId && !string.IsNullOrWhiteSpace(itemId))
        {
            if (!TryCoreCall(NativeMethods.cs_cart_add_item_by_id(_cart, itemId, 1), $"add {itemId}"))
            {
                return;
            }

            RefreshFromCoreJson();
        }
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

        if (sender is Button button && button.Tag is string centsText && long.TryParse(centsText, out var cents))
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
                        FormatCents(line.LineTotalCents)));
                }
            }

            TotalValueText.Text = FormatCents(snapshot.TotalCents);
            GivenValueText.Text = FormatCents(snapshot.GivenCents);
            ChangeValueText.Text = FormatCents(snapshot.ChangeCents);
            _currentGivenCents = snapshot.GivenCents;
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

    private static string FormatCents(long cents)
    {
        var value = cents / 100.0;
        return string.Format(CultureInfo.InvariantCulture, "CHF {0:0.00}", value);
    }

    private sealed record CartSnapshot(
        [property: JsonPropertyName("lines")] CartLine[]? Lines,
        [property: JsonPropertyName("total_cents")] long TotalCents,
        [property: JsonPropertyName("given_cents")] long GivenCents,
        [property: JsonPropertyName("change_cents")] long ChangeCents);

    private sealed record CartLine(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("unit_cents")] long UnitCents,
        [property: JsonPropertyName("qty")] int Qty,
        [property: JsonPropertyName("line_total_cents")] long LineTotalCents);

    private sealed record CartLineView(string Name, int Quantity, string LineTotalDisplay);
}
