using WitchDrawer.Core.Localization;
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
    private const uint ShellChangeAssociationChanged = 0x08000000;
    private const uint ShellNotifyFlushNoWait = 0x2000;
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const uint WindowMessageCommand = 0x0111; // WM_COMMAND
    // 桌面视图“显示桌面图标”的切换命令（未公开命令 ID，与右键菜单里同一项）。
    private const nint ToggleDesktopIconsCommandId = 0x7402;
    private const int TogglePollIntervalMs = 50;
    private const int ToggleSettleTimeoutMs = 3000;
    private static readonly nint BroadcastWindow = 0xFFFF;

    // 双击可能连发：串行化切换，避免两次后台切换交错轮询。
    private static readonly SemaphoreSlim ToggleGate = new(1, 1);
    private static readonly DesktopListViewHitTester ListViewHitTester = new(AccessibleDesktopHitTest.OpenSession);

    public static bool IsHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            ExplorerAdvancedRegistryPath,
            writable: false);
        return IsHiddenRegistryValue(key?.GetValue(HideIconsValueName));
    }

    /// <summary>
    /// 切换桌面图标的显示状态，返回切换后的隐藏状态。
    /// 切换通过资源管理器自己的“显示桌面图标”命令完成，使视图内部状态、
    /// 注册表与右键菜单保持同步；原生命令不可用时才退回直改注册表和窗口的旧路径。
    /// </summary>
    public static async Task<bool> ToggleHiddenAsync(
        CancellationToken cancellationToken = default)
    {
        await ToggleGate.WaitAsync(cancellationToken);
        try
        {
            return await RunToggleHiddenAsync(ToggleHidden, cancellationToken);
        }
        finally
        {
            ToggleGate.Release();
        }
    }

    internal static Task<bool> RunToggleHiddenAsync(
        Func<bool> toggle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        return Task.Run(toggle, cancellationToken);
    }

    private static bool ToggleHidden() => ApplyToggle(
        FindDesktopShellWindows,
        IsWindowVisible,
        IsHidden,
        PostDesktopToggleCommand,
        static milliseconds => Thread.Sleep(milliseconds),
        WriteHiddenRegistry,
        SetDesktopListViewHidden,
        NotifyDesktopIconSettingChanged);

    private static bool PostDesktopToggleCommand(nint window) => PostMessageW(
        window,
        WindowMessageCommand,
        ToggleDesktopIconsCommandId,
        nint.Zero);

    /// <summary>
    /// 切换基准取窗口实际可见性，而不是注册表或缓存：历史版本用 ShowWindow 直改
    /// 窗口，可能留下注册表、视图内部状态、窗口可见性三者互相矛盾的状态，
    /// 只有用户看到的状态才能解释“双击切换”的语义。
    /// </summary>
    internal static bool ApplyToggle(
        Func<DesktopShellHandles> findDesktopShell,
        Func<nint, bool> isWindowVisible,
        Func<bool> readHiddenRegistry,
        Func<nint, bool> postToggleCommand,
        Action<int> wait,
        Action<bool> writeRegistry,
        Action<nint, bool> setDesktopListViewHidden,
        Action notifySettingChanged)
    {
        ArgumentNullException.ThrowIfNull(findDesktopShell);
        ArgumentNullException.ThrowIfNull(isWindowVisible);
        ArgumentNullException.ThrowIfNull(readHiddenRegistry);
        ArgumentNullException.ThrowIfNull(postToggleCommand);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(writeRegistry);
        ArgumentNullException.ThrowIfNull(setDesktopListViewHidden);
        ArgumentNullException.ThrowIfNull(notifySettingChanged);

        var shell = findDesktopShell();
        if (shell.ListView == nint.Zero)
        {
            // 观测不到桌面视图时无从校验真实状态，退回注册表翻转的旧语义。
            var flipped = !readHiddenRegistry();
            writeRegistry(flipped);
            notifySettingChanged();
            return flipped;
        }

        var hideTarget = isWindowVisible(shell.ListView);
        var commandTargets = CollectCommandTargets(shell);
        if (commandTargets.Count == 0)
        {
            FallBackToDirectWrite(
                hideTarget,
                shell.ListView,
                writeRegistry,
                setDesktopListViewHidden,
                notifySettingChanged);
            return hideTarget;
        }

        foreach (var commandTarget in commandTargets)
        {
            if (!postToggleCommand(commandTarget))
            {
                continue;
            }

            // A successfully posted toggle is not safe to retry: it may still be queued.
            // Registry writes may precede the visual change, so wait for visibility only.
            for (var elapsed = 0; elapsed < ToggleSettleTimeoutMs; elapsed += TogglePollIntervalMs)
            {
                if (isWindowVisible(shell.ListView) == !hideTarget)
                {
                    writeRegistry(hideTarget);
                    return hideTarget;
                }
                wait(TogglePollIntervalMs);
            }

            if (isWindowVisible(shell.ListView) == !hideTarget)
            {
                writeRegistry(hideTarget);
                return hideTarget;
            }
            throw new TimeoutException(Strings.Get("WindowsHasNotFinishedTogglingDesktopIconsWaitFor"));
        }

        // Only fall back when no command was queued at all.
        FallBackToDirectWrite(
            hideTarget,
            shell.ListView,
            writeRegistry,
            setDesktopListViewHidden,
            notifySettingChanged);
        return hideTarget;
    }

    private static void FallBackToDirectWrite(
        bool hidden,
        nint desktopListView,
        Action<bool> writeRegistry,
        Action<nint, bool> setDesktopListViewHidden,
        Action notifySettingChanged)
    {
        writeRegistry(hidden);
        setDesktopListViewHidden(desktopListView, hidden);
        notifySettingChanged();
    }

    private static List<nint> CollectCommandTargets(DesktopShellHandles shell)
    {
        var targets = new List<nint>(2);
        // SHELLDLL_DefView owns the command. Progman/WorkerW can accept the
        // posted message without handling it, which otherwise causes a timeout.
        if (shell.ShellView != nint.Zero)
        {
            targets.Add(shell.ShellView);
        }

        if (shell.HostWindow != nint.Zero && !targets.Contains(shell.HostWindow))
        {
            targets.Add(shell.HostWindow);
        }

        return targets;
    }

    private static void WriteHiddenRegistry(bool hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            ExplorerAdvancedRegistryPath,
            writable: true)
            ?? throw new InvalidOperationException(Strings.Get("CannotOpenWindowsDesktopIconSettings"));
        key.SetValue(
            HideIconsValueName,
            hidden ? 1 : 0,
            RegistryValueKind.DWord);
    }

    private static void SetDesktopListViewHidden(nint desktopListView, bool hidden)
    {
        ShowWindow(desktopListView, hidden ? ShowWindowHide : ShowWindowShow);
    }

    private static void NotifyDesktopIconSettingChanged()
    {
        SHChangeNotify(
            ShellChangeAssociationChanged,
            ShellNotifyFlushNoWait,
            nint.Zero,
            nint.Zero);
        SendNotifyMessageW(
            BroadcastWindow,
            WindowMessageSettingChange,
            nint.Zero,
            nint.Zero);
    }

    public static async Task<bool> IsBlankDesktopPointAsync(int screenX, int screenY)
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
            return await ListViewHitTester.IsBlankAsync(desktopListView, screenX, screenY).ConfigureAwait(false);
        }

        return IsDesktopHostWindow(clickedWindow);
    }

    internal static bool IsHiddenRegistryValue(object? value) => value switch
    {
        int intValue => intValue != 0,
        long longValue => longValue != 0,
        _ => false
    };

    internal readonly record struct DesktopShellHandles(
        nint HostWindow,
        nint ShellView,
        nint ListView);

    private static DesktopShellHandles FindDesktopShellWindows()
    {
        var hostWindow = FindWindowW("Progman", null);
        var shellView = FindWindowExW(
            hostWindow,
            nint.Zero,
            "SHELLDLL_DefView",
            null);
        if (shellView == nint.Zero)
        {
            // Win10 的桌面视图可能挂在 WorkerW 下：枚举顶层窗口寻找携带视图的那个。
            EnumWindows(
                (window, _) =>
                {
                    shellView = FindWindowExW(
                        window,
                        nint.Zero,
                        "SHELLDLL_DefView",
                        null);
                    if (shellView != nint.Zero)
                    {
                        hostWindow = window;
                        return false;
                    }

                    return true;
                },
                nint.Zero);
        }

        var listView = shellView == nint.Zero
            ? nint.Zero
            : FindWindowExW(shellView, nint.Zero, "SysListView32", null);
        return new DesktopShellHandles(hostWindow, shellView, listView);
    }

    private static nint FindDesktopListView() => FindDesktopShellWindows().ListView;

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
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessageW(
        nint window,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        nint item1,
        nint item2);
}
