using System.Collections.ObjectModel;
using System.Windows;

namespace CashSloth.App;

public partial class CustomerDisplayWindow : Window
{
    private readonly ObservableCollection<CartLineView> _lines = new();

    public CustomerDisplayWindow()
    {
        InitializeComponent();
        CartLinesGrid.ItemsSource = _lines;
    }

    public void Update(CartSnapshot snapshot)
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

        TotalValueText.Text = CurrencyFormatter.FormatCents(snapshot.TotalCents);
        GivenValueText.Text = CurrencyFormatter.FormatCents(snapshot.GivenCents);
        ChangeValueText.Text = CurrencyFormatter.FormatCents(snapshot.ChangeCents);
    }
}
