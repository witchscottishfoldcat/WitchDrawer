using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 位置锁、主题/透明度应用、DPI 度量与激活状态。
/// </summary>
public partial class DesktopBoxWindow
{
    public void SetPositionLocked(bool isPositionLocked)
    {
        if (_isPositionLocked == isPositionLocked)
        {
            return;
        }

        _isPositionLocked = isPositionLocked;

        // A lock transition must never leave a control holding mouse capture.
        // In particular, the old drawer-cover Thumb path could keep a completed
        // locked gesture around and make the next unlocked gesture appear inert.
        if (Mouse.Captured is DependencyObject captured
            && (ReferenceEquals(captured, this) || IsAncestorOf(captured)))
        {
            Mouse.Capture(null);
        }
    }

    public void SetPositionChangedCallback(Func<Guid, Task> callback)
    {
        _positionChangedCallback = callback;
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateIconDisplayMetrics(e.NewDpi);
    }

    private void UpdateIconDisplayMetrics(DpiScale dpi)
    {
        ViewModel.UpdateIconDisplayMetrics(dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        ApplyThemeAppearance();
    }

    private void OnBoxOpacityChanged(object? sender, ThemeBoxOpacityChangedEventArgs e)
    {
        OnDesktopBoxAppearanceChanged(sender, e.Theme);
    }

    private void OnDesktopBoxAppearanceChanged(object? sender, AppTheme theme)
    {
        if (theme != AppThemeManager.CurrentTheme || _isBoxOpacityRefreshQueued)
        {
            return;
        }

        _isBoxOpacityRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                _isBoxOpacityRefreshQueued = false;
                AppThemeManager.ApplyDesktopBoxResources(Resources);
            });
    }

    private void ApplyThemeAppearance()
    {
        AppThemeManager.ApplyDesktopBoxResources(Resources);
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        QueueSendToBottom();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
        QueueSendToBottom();
    }
}
