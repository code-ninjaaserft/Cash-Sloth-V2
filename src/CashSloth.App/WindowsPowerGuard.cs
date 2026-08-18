using System.Runtime.InteropServices;

namespace CashSloth.App;

internal sealed class WindowsPowerGuard : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002
    }

    private bool _isActive;

    internal bool TryKeepAwake(out string? error)
    {
        error = null;

        try
        {
            var result = SetThreadExecutionState(
                ExecutionState.Continuous |
                ExecutionState.SystemRequired |
                ExecutionState.DisplayRequired);
            if (result == 0)
            {
                error = "Windows rejected the keep-awake request.";
                return false;
            }

            _isActive = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (!_isActive)
        {
            return;
        }

        try
        {
            SetThreadExecutionState(ExecutionState.Continuous);
        }
        catch
        {
            // The process is closing; restoring power state is best effort.
        }

        _isActive = false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
}
