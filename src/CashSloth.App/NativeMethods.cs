using System.Runtime.InteropServices;

namespace CashSloth.App;

internal static class NativeMethods
{
    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_init();

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cs_shutdown();

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_get_version(out IntPtr json);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr cs_last_error();

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cs_free(IntPtr p);
}
