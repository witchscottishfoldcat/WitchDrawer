using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Shell;

/// <summary>
/// Win32 popup-menu declarations for the tray context menu. Moved verbatim from App.xaml.cs.
/// </summary>
public static class NativePopupMenu
{
    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(nint hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(nint hMenu);
}
