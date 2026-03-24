using System.Collections.ObjectModel;
using System.Windows;

namespace CashSloth.App;

public partial class CustomerDisplayWindow : Window
{
    private readonly ObservableCollection<CartLineView> _lines = new();
    private UiLanguage _language = AppSettings.Default.Language;

    public CustomerDisplayWindow()
    {
        InitializeComponent();
        CartLinesGrid.ItemsSource = _lines;
        ApplyLocalization(_language);
    }

    internal void ApplyLocalization(UiLanguage language)
    {
        _language = language;
        Title = UiLocalizer.Get(_language, "customer.title");
        TotalLabelText.Text = UiLocalizer.Get(_language, "label.total");
        GivenLabelText.Text = UiLocalizer.Get(_language, "label.given");
        ChangeLabelText.Text = UiLocalizer.Get(_language, "label.change");

        if (CartLinesGrid.Columns.Count >= 3)
        {
            CartLinesGrid.Columns[0].Header = UiLocalizer.Get(_language, "column.item");
            CartLinesGrid.Columns[1].Header = UiLocalizer.Get(_language, "column.qty");
            CartLinesGrid.Columns[2].Header = UiLocalizer.Get(_language, "column.line_total");
        }
    }

    internal void Update(CartSnapshot snapshot)
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
