using System.Windows;
using System.Runtime.InteropServices;

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

    private void OnGetVersionClick(object sender, RoutedEventArgs e)
    {
        var result = NativeMethods.cs_get_version(out var jsonPtr);
        if (result != 0)
        {
            var errorPtr = NativeMethods.cs_last_error();
            var errorMessage = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error.";
            VersionJsonText.Text = $"Failed to get version JSON ({result}): {errorMessage}";
            return;
        }

        var json = Marshal.PtrToStringUTF8(jsonPtr) ?? string.Empty;
        NativeMethods.cs_free(jsonPtr);
        VersionJsonText.Text = json.Length > 0 ? json : "Version JSON returned empty.";
    }
}
