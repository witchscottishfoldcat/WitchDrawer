using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WitchDrawer.Native.Files;

/// <summary>Owns an HICON or HBITMAP until the UI adapter has copied its pixels.</summary>
public sealed class NativeShellImage : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeShellImage(nint handle, bool isBitmap) : base(ownsHandle: true)
    {
        SetHandle(handle);
        IsBitmap = isBitmap;
    }

    public bool IsBitmap { get; }
    protected override bool ReleaseHandle() => IsBitmap ? DeleteObject(handle) : DestroyIcon(handle);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
}
