using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

internal static class DesktopShellHost
{
    // Windows 11 retains the existing Progman ownership path. On Windows 10,
    // the desktop view can live in a separate WorkerW above Progman, so owning
    // a box from Progman alone does not keep it above that desktop surface.
    internal static bool UsesLegacyDesktopHost =>
        OperatingSystem.IsWindowsVersionAtLeast(10)
        && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    internal static nint ResolveOwner(nint shellWindow) => ResolveOwner(
        shellWindow,
        UsesLegacyDesktopHost,
        HasDesktopView,
        FindDesktopWorker);

    internal static nint ResolveOwner(
        nint shellWindow,
        bool useLegacyDesktopHost,
        Func<nint, bool> hasDesktopView,
        Func<nint, nint> findDesktopWorker)
    {
        if (shellWindow == nint.Zero
            || !useLegacyDesktopHost
            || hasDesktopView(shellWindow))
        {
            return shellWindow;
        }

        var worker = findDesktopWorker(shellWindow);
        return worker != nint.Zero ? worker : shellWindow;
    }

    private static bool HasDesktopView(nint window) =>
        FindWindowExW(window, nint.Zero, "SHELLDLL_DefView", null) != nint.Zero;

    private static nint FindDesktopWorker(nint shellWindow)
    {
        GetWindowThreadProcessId(shellWindow, out var shellProcessId);
        if (shellProcessId == 0)
        {
            return nint.Zero;
        }

        nint result = nint.Zero;
        // Enumerate only existing top-level windows; do not create WorkerW or
        // send undocumented messages to Explorer. Hidden desktop icons are OK:
        // the host must be visible, but its view/list does not have to be.
        EnumWindows((window, _) =>
        {
            var className = new StringBuilder(64);
            GetClassNameW(window, className, className.Capacity);
            if (className.ToString() != "WorkerW" || !IsWindowVisible(window))
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId != shellProcessId || !HasDesktopView(window))
            {
                return true;
            }

            result = window;
            return false;
        }, nint.Zero);
        return result;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
