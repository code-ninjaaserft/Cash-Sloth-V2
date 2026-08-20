using System.Runtime.InteropServices;

namespace CashSloth.Server.Infrastructure;

public static class AuthenticodeVerifier
{
    private static readonly Guid VerifyAction = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool HasValidSignature(string filePath, out string? error)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var trustData = new WinTrustData(filePointer);
        var trustPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            Marshal.StructureToPtr(trustData, trustPointer, false);
            var result = WinVerifyTrust(IntPtr.Zero, VerifyAction, trustPointer);
            error = result == 0 ? null : $"Authenticode-Prüfung meldete 0x{result:X8}.";
            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustData>(trustPointer);
            Marshal.FreeHGlobal(trustPointer);
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2;
            RevocationChecks = 0;
            UnionChoice = 1;
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0x00001000;
            UiContext = 0;
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr windowHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);
}
