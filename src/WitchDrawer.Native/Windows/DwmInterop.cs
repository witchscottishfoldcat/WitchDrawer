using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

public static class DwmInterop
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
