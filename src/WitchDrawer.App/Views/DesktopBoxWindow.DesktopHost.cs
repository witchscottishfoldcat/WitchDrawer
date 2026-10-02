using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Native.Windows;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

/// <summary>
/// Win32 桌面宿主适配：Z 序沉底、前台所有权、消息钩子。底层行为在 Native 的 DesktopToolWindow。
/// </summary>
public partial class DesktopBoxWindow
{
    internal event Action? DesktopLayerChanged;

    private void NotifyDesktopLayerChanged()
    {
        if (_forceClose || Visibility != Visibility.Visible)
        {
            return;
        }

        try
        {
            DesktopLayerChanged?.Invoke();
        }
        catch (Exception exception)
        {
            ViewModel.Logger.Error(exception, "Failed to queue desktop layer repair.");
        }
    }

    private void SendToBottom()
    {
        if (DesktopWindowLayer.IsEnabled)
        {
            NotifyDesktopLayerChanged();
            return;
        }

        if (!ShouldSendToBottom(_desktopIsForeground))
        {
            return;
        }

        // Shell ownership keeps the box visible through Win+D. Keep it at the
        // bottom of the normal band only while the desktop is not foreground.
        // Moving an owned box during Show Desktop also moves Progman's owner
        // chain and would bring ordinary app windows back above the desktop.
        _nativeWindow?.SendToBottom();
    }

    internal static bool ShouldSendToBottom(bool isDesktopForeground) =>
        !isDesktopForeground;

    public void QueueSendToBottom()
    {
        if (DesktopWindowLayer.IsEnabled)
        {
            NotifyDesktopLayerChanged();
            return;
        }

        SendToBottom();
        Dispatcher.BeginInvoke(new Action(SendToBottom), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// 把所有桌面盒压回桌面层。弹窗打开时属主链被 Windows 整体提前，单个盒子沉底不够，
    /// 必须遍历所有盒子窗口统一复位。
    /// </summary>
    internal static void QueueSendToBottomAll()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.QueueSendToBottom();
        }
    }

    /// <summary>
    /// 断开弹窗 HWND 的属主关系：之后对弹窗的置顶/沉底不再沿属主链
    /// （盒子→桌面壳→所有盒子）传播。在 Opened 时同步执行，消除窗口期。
    /// </summary>
    private void DetachDrawerPopupOwner()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowLongPtr(popupSource.Handle, WindowOwnerIndex, 0);
        }
    }

    /// <summary>
    /// 弹窗按"菜单"语义激活：置顶并获取前台。弹窗已断开属主（Opened 时），激活只影响
    /// 弹窗自身——盒子不动。激活后 WPF 原生的 StaysOpen=False 完整生效：
    /// 点击桌面/其他程序/其他盒子都会自动收起，无需额外兜底。
    /// </summary>
    private void BringDrawerPopupToFront()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowPos(
                popupSource.Handle,
                WindowPositionTopmost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
            SetForegroundWindow(popupSource.Handle);
        }
    }

    public nint NativeHandle => _nativeWindow?.Handle ?? nint.Zero;

    public bool IsNativeWindowAlive => _nativeWindow?.IsAlive == true;

    public bool RefreshDesktopHost()
    {
        if (DesktopWindowLayer.IsEnabled)
        {
            NotifyDesktopLayerChanged();
            return IsNativeWindowAlive;
        }

        return _nativeWindow?.TryAttachToDesktop() == true;
    }

    public void SetDesktopForeground(bool isForeground)
    {
        if (DesktopWindowLayer.IsEnabled)
        {
            NotifyDesktopLayerChanged();
            return;
        }

        if (isForeground)
        {
            _nativeWindow?.RefreshDesktopHostForShowDesktop();
        }

        // When Show Desktop is active, leave Explorer's owner-chain Z order
        // untouched. On exit, return the boxes behind ordinary app windows.
        _desktopIsForeground = isForeground;
        SendToBottom();
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (DesktopToolWindow.IsMouseActivationMessage(message))
        {
            // Active window tracking can bypass WS_EX_NOACTIVATE. Always reject
            // mouse activation while preserving input, including on Windows 11.
            if (!DesktopWindowLayer.IsEnabled)
            {
                // Only the legacy path owns the box from Explorer; detach before
                // mouse input so the shell cannot record it as its last active popup.
                _nativeWindow?.SuspendDesktopOwnershipForMouseInput();
            }
            handled = true;
            return DesktopToolWindow.GetMouseActivateWithoutActivationResult();
        }

        if (DesktopWindowLayer.IsEnabled)
        {
            if (DesktopToolWindow.IsMinimizeSystemCommand(message, wordParameter))
            {
                handled = true;
            }
            if (DesktopWindowLayer.IsLayerChangeMessage(message, wordParameter, longParameter))
            {
                NotifyDesktopLayerChanged();
            }
            return nint.Zero;
        }

        if (DesktopToolWindow.IsMouseInteractionCompletionMessage(message))
        {
            QueueRestoreDesktopOwnershipAfterMouseInput();
        }

        if (DesktopToolWindow.IsMinimizeSystemCommand(message, wordParameter))
        {
            // Win+D / Show Desktop normally minimizes top-level windows. A desktop
            // box is desktop furniture, so consume the minimize command.
            handled = true;
        }

        return nint.Zero;
    }

    private void QueueRestoreDesktopOwnershipAfterMouseInput()
    {
        if (DesktopWindowLayer.IsEnabled || _desktopOwnershipRestoreQueued)
        {
            return;
        }

        _desktopOwnershipRestoreQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                _desktopOwnershipRestoreQueued = false;
                if (!_forceClose)
                {
                    _nativeWindow?.RestoreDesktopOwnershipAfterMouseInput(_desktopIsForeground);
                }
            });
    }

    private void OnWindowPreviewMouseUpForDesktopOwnership(
        object sender,
        MouseButtonEventArgs e)
    {
        // WPF fallback for controls that complete input before the HWND hook
        // observes the native button-up message.
        QueueRestoreDesktopOwnershipAfterMouseInput();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (DesktopWindowLayer.IsEnabled)
        {
            if (WindowState == WindowState.Minimized)
            {
                NotifyDesktopLayerChanged();
            }
            return;
        }

        if (_forceClose
            || WindowState != WindowState.Minimized
            || _restoreAfterMinimizeQueued)
        {
            return;
        }

        // Some shell versions minimize via ShowWindow instead of WM_SYSCOMMAND.
        // Restore after the shell's burst of Z-order changes has settled.
        _restoreAfterMinimizeQueued = true;
        FireAndForget.Run(
                RestoreAfterShellMinimizeAsync(),
                ViewModel.Logger,
                $"Failed to restore box window {ViewModel.BoxId:N} after shell minimize.");
    }

    private async Task RestoreAfterShellMinimizeAsync()
    {
        await Task.Delay(120).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _restoreAfterMinimizeQueued = false;
            if (!_forceClose && WindowState == WindowState.Minimized)
            {
                _nativeWindow?.RestoreWithoutActivation();
                // RestoreWithoutActivation no longer changes Z order. Apply exactly
                // one layer operation based on the stabilized desktop state.
                SendToBottom();
            }
        });
    }
}
