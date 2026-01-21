using System.Windows;

namespace CashSloth.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnInitCoreClick(object sender, RoutedEventArgs e)
    {
        var result = NativeMethods.cs_init();
        InitResultText.Text = $"Core init result: {result}";
    }
}
