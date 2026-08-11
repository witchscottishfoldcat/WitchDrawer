using System.Runtime.InteropServices;
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

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string className,
        string? windowName);

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

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        nint item1,
        nint item2);
}
