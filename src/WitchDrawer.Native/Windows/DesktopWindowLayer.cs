using System.Runtime.InteropServices;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.Native.Windows;

/// <summary>Unowned Windows 11 boxes, immediately above the real desktop view host.</summary>
public static class DesktopWindowLayer
{
    public static bool IsEnabled => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static void Configure(nint handle)
    {
        if (handle == 0 || !IsWindow(handle))
        {
            throw new ArgumentException("A live HWND is required.", nameof(handle));
        }
        var style = GetWindowLongPtrW(handle, -20);
        SetWindowLongPtr(handle, -20, (style | 0x08000080) & ~((nint)0x00040000));
        ClearOwner(handle);
        // Do not change WS_POPUP/WS_CHILD after WPF creates the window.
        SetWindowPos(handle, 0, 0, 0, 0, 0,
            SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate | 0x0020);
    }

    public static nint Maintain(nint cachedHost, IEnumerable<nint> handles)
    {
        var shell = GetShellWindow();
        GetWindowThreadProcessId(shell, out var shellProcess);
        GetWindowThreadProcessId(cachedHost, out var cachedProcess);
        var host = cachedHost != 0 && IsWindow(cachedHost) && IsWindowVisible(cachedHost)
            && shellProcess != 0 && shellProcess == cachedProcess && DesktopShellHost.HasDesktopView(cachedHost)
            ? cachedHost
            : DesktopShellHost.ResolveOwner(shell, true, DesktopShellHost.HasDesktopView, DesktopShellHost.FindDesktopWorker);
        if (host == 0 || !IsWindow(host) || !IsWindowVisible(host))
        {
            return 0;
        }
        MaintainAboveHost(host, handles);
        return host;
    }

    // Caller excludes deliberately hidden boxes; never activate, resize or move them.
    internal static void MaintainAboveHost(nint host, IEnumerable<nint> handles)
    {
        var anchor = host;
        foreach (var handle in handles)
        {
            if (handle == 0 || handle == host || !IsWindow(handle))
            {
                continue;
            }
            ClearOwner(handle);
            if (IsIconic(handle) || !IsWindowVisible(handle))
            {
                ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
            }
            var above = GetWindow(anchor, 3); // GW_HWNDPREV
            if (above != handle)
            {
                // Inserting after a topmost predecessor would promote the box.
                var insertion = above != 0 && (GetWindowLongPtrW(above, -20) & 8) != 0 ? (nint)(-2) : above;
                SetWindowPos(handle, insertion, 0, 0, 0, 0,
                    SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
            }
            anchor = handle;
        }
    }

    private static void ClearOwner(nint handle)
    {
        if (GetWindow(handle, 4) != 0)
        {
            SetWindowLongPtr(handle, WindowOwnerIndex, 0);
        }
    }

    public static bool IsLayerChangeMessage(int message, nint wordParameter, nint longParameter) =>
        (message == 0x0005 && wordParameter == 1) // WM_SIZE / SIZE_MINIMIZED
        || (message == 0x0047 && longParameter != 0
            && AffectsDesktopLayer(Marshal.PtrToStructure<WindowPosition>(longParameter).Flags));

    internal static bool AffectsDesktopLayer(uint flags) =>
        (flags & SetWindowPosNoZOrder) == 0 || (flags & 0x0040) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Window;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
