using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WitchDrawer.Native.Windows;

public static class DesktopIconVisibility
{
    private const string ExplorerAdvancedRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string HideIconsValueName = "HideIcons";
    private const uint WindowMessageSettingChange = 0x001A;
    private const uint SendMessageAbortIfHung = 0x0002;
    private const uint ShellChangeAssociationChanged = 0x08000000;
    private const uint ShellNotifyFlushNoWait = 0x2000;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const uint ListViewHitTest = 0x1000 + 18; // LVM_HITTEST
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemoryCommitReserve = 0x3000;
    private const uint MemoryRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private static readonly nint BroadcastWindow = 0xFFFF;

    public static bool IsHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            ExplorerAdvancedRegistryPath,
            writable: false);
        return IsHiddenRegistryValue(key?.GetValue(HideIconsValueName));
    }

    public static Task SetHiddenAsync(
        bool hidden,
        CancellationToken cancellationToken = default) =>
        RunSetHiddenAsync(SetHidden, hidden, cancellationToken);

    internal static Task RunSetHiddenAsync(
        Action<bool> setHidden,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setHidden);
        return Task.Run(() => setHidden(hidden), cancellationToken);
    }

    public static void SetHidden(bool hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            ExplorerAdvancedRegistryPath,
            writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 桌面图标设置。");
        key.SetValue(
            HideIconsValueName,
            hidden ? 1 : 0,
            RegistryValueKind.DWord);

        SendMessageTimeoutW(
            BroadcastWindow,
            WindowMessageSettingChange,
            nint.Zero,
            ExplorerAdvancedRegistryPath,
            SendMessageAbortIfHung,
            1000,
            out _);
        SHChangeNotify(
            ShellChangeAssociationChanged,
            ShellNotifyFlushNoWait,
            nint.Zero,
            nint.Zero);

        var desktopListView = FindDesktopListView();
        if (desktopListView != nint.Zero)
        {
            ShowWindow(desktopListView, hidden ? ShowWindowHide : ShowWindowShow);
        }
    }

    public static bool IsBlankDesktopPoint(int screenX, int screenY)
    {
        var clickedWindow = WindowFromPoint(new NativePoint(screenX, screenY));
        if (clickedWindow == nint.Zero)
        {
            return false;
        }

        var desktopListView = FindDesktopListView();
        if (desktopListView != nint.Zero
            && IsWindowVisible(desktopListView)
            && clickedWindow == desktopListView)
        {
            return IsBlankListViewPoint(desktopListView, screenX, screenY);
        }

        return IsDesktopHostWindow(clickedWindow);
    }

    internal static bool IsHiddenRegistryValue(object? value) => value switch
    {
        int intValue => intValue != 0,
        long longValue => longValue != 0,
        _ => false
    };

    private static nint FindDesktopListView()
    {
        var programManager = FindWindowW("Progman", null);
        var shellView = FindWindowExW(
            programManager,
            nint.Zero,
            "SHELLDLL_DefView",
            null);
        if (shellView == nint.Zero)
        {
            EnumWindows(
                (window, _) =>
                {
                    shellView = FindWindowExW(
                        window,
                        nint.Zero,
                        "SHELLDLL_DefView",
                        null);
                    return shellView == nint.Zero;
                },
                nint.Zero);
        }

        return shellView == nint.Zero
            ? nint.Zero
            : FindWindowExW(shellView, nint.Zero, "SysListView32", null);
    }

    private static bool IsBlankListViewPoint(nint listView, int screenX, int screenY)
    {
        var point = new NativePoint(screenX, screenY);
        if (!ScreenToClient(listView, ref point))
        {
            return false;
        }

        GetWindowThreadProcessId(listView, out var processId);
        var process = OpenProcess(ProcessVmOperation | ProcessVmWrite, false, processId);
        if (process == nint.Zero)
        {
            return false;
        }

        var size = (nuint)Marshal.SizeOf<ListViewHitTestInfo>();
        var remoteBuffer = nint.Zero;
        try
        {
            remoteBuffer = VirtualAllocEx(
                process,
                nint.Zero,
                size,
                MemoryCommitReserve,
                PageReadWrite);
            if (remoteBuffer == nint.Zero)
            {
                return false;
            }

            var hitTest = new ListViewHitTestInfo
            {
                Point = point,
                Item = -1,
                SubItem = -1,
                Group = -1
            };
            if (!WriteProcessMemory(process, remoteBuffer, ref hitTest, size, out var bytesWritten)
                || bytesWritten != size)
            {
                return false;
            }

            var sent = SendMessageTimeoutPointer(
                listView,
                ListViewHitTest,
                nint.Zero,
                remoteBuffer,
                SendMessageAbortIfHung,
                100,
                out var result);
            return sent != nint.Zero && result == new nint(-1);
        }
        finally
        {
            if (remoteBuffer != nint.Zero)
            {
                VirtualFreeEx(process, remoteBuffer, 0, MemoryRelease);
            }

            CloseHandle(process);
        }
    }

    internal static bool IsDesktopHostClass(string? className) =>
        string.Equals(className, "Progman", StringComparison.Ordinal)
        || string.Equals(className, "WorkerW", StringComparison.Ordinal)
        || string.Equals(className, "SHELLDLL_DefView", StringComparison.Ordinal);

    private static bool IsDesktopHostWindow(nint window)
    {
        var className = new StringBuilder(64);
        return GetClassNameW(window, className, className.Capacity) > 0
            && IsDesktopHostClass(className.ToString());
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewHitTestInfo
    {
        public NativePoint Point;
        public uint Flags;
        public int Item;
        public int SubItem;
        public int Group;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(
        nint window,
        uint message,
        nint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nint result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeoutPointer(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(
        nint process,
        nint address,
        nuint size,
        uint allocationType,
        uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        nint process,
        nint address,
        ref ListViewHitTestInfo buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(
        nint process,
        nint address,
        nuint size,
        uint freeType);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        nint item1,
        nint item2);
}
