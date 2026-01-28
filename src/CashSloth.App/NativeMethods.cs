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
    internal static extern int cs_catalog_load_json([MarshalAs(UnmanagedType.LPUTF8Str)] string json);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_new(out IntPtr cart);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_free(IntPtr cart);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_clear(IntPtr cart);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_add_item_by_id(IntPtr cart, [MarshalAs(UnmanagedType.LPUTF8Str)] string itemId, int qty);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_remove_line(IntPtr cart, int lineIndex);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_cart_get_lines_json(IntPtr cart, out IntPtr json);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cs_payment_set_given_cents(IntPtr cart, long givenCents);

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr cs_last_error();

    [DllImport("CashSlothCore.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cs_free(IntPtr p);
}
