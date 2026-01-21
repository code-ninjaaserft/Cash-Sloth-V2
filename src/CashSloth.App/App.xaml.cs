using System.Windows;

namespace CashSloth.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = NativeMethods.cs_init();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NativeMethods.cs_shutdown();
        base.OnExit(e);
    }
}
