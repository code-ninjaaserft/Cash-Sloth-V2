using System.Runtime.InteropServices;

namespace CashSloth.App;

internal static class WindowsSessionSecurity
{
    internal static bool TryLockWorkstation(out string? error)
    {
        error = null;

        try
        {
            if (LockWorkStation())
            {
                return true;
            }

            error = "Windows rejected the lock request.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
