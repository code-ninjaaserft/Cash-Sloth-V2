using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CashSloth.Server.Infrastructure;

public sealed class WindowsPowerGuard : IDisposable
{
    private bool _active;

    public bool IsActive => _active;

    public void Activate()
    {
        if (_active)
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Wake-Guard wird nur unter Windows unterstützt.");
        }
        var result = SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
        if (result == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows-Wake-Guard konnte nicht aktiviert werden.");
        }
        _active = true;
    }

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }
        SetThreadExecutionState(ExecutionState.Continuous);
        _active = false;
    }

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);
}
