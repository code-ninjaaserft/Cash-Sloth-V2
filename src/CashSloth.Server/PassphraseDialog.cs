using System.Windows;
using System.Windows.Controls;

namespace CashSloth.Server;

public sealed class PassphraseDialog : Window
{
    private readonly PasswordBox _passwordBox = new();

    public PassphraseDialog(string title, string instruction)
    {
        Title = title;
        Width = 430;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = instruction, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 0, 3, 10) });
        panel.Children.Add(_passwordBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true };
        var confirm = new Button { Content = "Bestätigen", IsDefault = true };
        confirm.Click += (_, _) =>
        {
            if (_passwordBox.Password.Length < 12)
            {
                System.Windows.MessageBox.Show(this, "Die Passphrase muss mindestens 12 Zeichen lang sein.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public string Passphrase => _passwordBox.Password;
}
