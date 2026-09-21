using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// user32 window-management declarations shared by App windows. Moved verbatim from
/// DesktopBoxWindow/GuideLineWindow so the App layer holds no raw P/Invoke of its own.
/// </summary>
public static class User32Interop
{
    public static readonly nint WindowPositionTopmost = -1;
    public const int WindowOwnerIndex = -8;
    public const uint SetWindowPosNoSize = 0x0001;
    public const uint SetWindowPosNoMove = 0x0002;
    public const uint SetWindowPosNoZOrder = 0x0004;
    public const uint SetWindowPosNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    public static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    public const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(NativePoint point, uint dwFlags);

    // 必须显式指定 CharSet.Unicode：默认 CharSet.None 会绑定 ANSI 版 GetMonitorInfoA，
    // 而 NativeMonitorInfo 按 Unicode 布局（ByValTStr SizeConst=32，cbSize=104），
    // GetMonitorInfoA 只接受 40/72 字节的 cbSize，会静默返回 false，导致召回屏幕中心被跳过。
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    public static int ToNativeCoordinate(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
}
