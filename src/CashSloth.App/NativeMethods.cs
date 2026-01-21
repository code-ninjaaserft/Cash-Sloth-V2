using System.Runtime.InteropServices;

namespace CashSloth.App;

internal static class NativeMethods
{
    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_init();

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cs_shutdown();
}
