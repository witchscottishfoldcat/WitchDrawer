using System.Windows;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    // 前台窗口、层叠状态与显示桌面（Win+D）处理。

    private void OnForegroundWindowChanged(nint windowHandle)
    {
        if (_closing)
        {
            return;
        }

        if (DesktopToolWindow.IsShowDesktopShortcutPressed())
        {
            Interlocked.Exchange(
                ref _showDesktopShortcutObservedUntilTick,
                Environment.TickCount64 + 750);
        }

        // Win+D emits a short burst of foreground changes (for example Progman,
        // WorkerW and transient shell windows). Applying every intermediate handle
        // moves all boxes up and down several times and produces a visible flash.
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _foregroundChangeCts, next);
        previous?.Cancel();
        previous?.Dispose();
        FireAndForget.Run(
                ApplyForegroundWindowAfterSettlingAsync(next),
                _logger,
                "Failed to apply foreground window state after settling.");
    }

    private async Task ApplyForegroundWindowAfterSettlingAsync(CancellationTokenSource changeCts)
    {
        var cancellationToken = changeCts.Token;
        try
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _closing))
            {
                return;
            }

            var windowHandle = ForegroundWindowMonitor.GetCurrentForegroundWindow();
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ApplyForegroundWindow(windowHandle);
                    }
                },
                System.Windows.Threading.DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _foregroundChangeCts, null, changeCts),
                    changeCts))
            {
                changeCts.Dispose();
            }
        }
    }

    private void ApplyForegroundWindow(nint windowHandle)
    {
        if (_closing || windowHandle == nint.Zero)
        {
            return;
        }

        var isDesktopWindow = ForegroundWindowMonitor.IsDesktopWindow(windowHandle);
        var isDesktopBoxWindow = _windows.Values.Any(
            window => window.NativeHandle == windowHandle);
        var desktopIsForeground = ResolveDesktopForegroundState(
            isDesktopWindow,
            isDesktopBoxWindow);
        SetDesktopForeground(desktopIsForeground);

        if (ShouldLowerMainWindowForShowDesktop(
                desktopIsForeground,
                Environment.TickCount64,
                Interlocked.Read(ref _showDesktopShortcutObservedUntilTick)))
        {
            Interlocked.Exchange(ref _showDesktopShortcutObservedUntilTick, 0);
            ShowDesktopActivated?.Invoke(this, EventArgs.Empty);
        }
    }


    internal static bool ResolveDesktopForegroundState(
        bool isDesktopWindow,
        bool isDesktopBoxWindow) =>
        isDesktopWindow && !isDesktopBoxWindow;

    internal static bool ShouldLowerMainWindowForShowDesktop(
        bool desktopIsForeground,
        long currentTick,
        long shortcutObservedUntilTick) =>
        desktopIsForeground
        && shortcutObservedUntilTick > 0
        && currentTick <= shortcutObservedUntilTick;

    private void SetDesktopForeground(bool isForeground)
    {
        if (_desktopIsForeground == isForeground)
        {
            return;
        }

        _desktopIsForeground = isForeground;
        foreach (var window in _windows.Values)
        {
            window.SetDesktopForeground(isForeground);
        }
    }
}
