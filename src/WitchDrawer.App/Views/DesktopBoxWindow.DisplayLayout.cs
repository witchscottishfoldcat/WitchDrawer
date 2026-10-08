using System.Windows;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

public partial class DesktopBoxWindow
{
    private readonly DesktopBoxDisplayLayout _displayLayout = new();
    private DispatcherTimer? _displayLayoutTimer;
    private bool _isDisplayLayoutRecoveryPending;
    private bool _isRestoringDisplayLayout;

    internal Point? PreferredWindowOriginPixels => _displayLayout.PreferredOriginPixels;

    internal void RememberPreferredWindowOriginPixels(Point origin) => _displayLayout.RememberPosition(origin);

    internal void RememberCurrentDisplayPosition()
    {
        if (TryGetWindowBoundsPixels(out var bounds))
        {
            RememberPreferredWindowOriginPixels(bounds.TopLeft);
        }
    }

    internal void InitializeDisplayPosition()
    {
        if (PreferredWindowOriginPixels is null)
        {
            RememberCurrentDisplayPosition();
        }
        if (_isDisplayLayoutRecoveryPending)
        {
            RequestDisplayLayoutRecovery();
        }
    }

    internal void RequestDisplayLayoutRecovery()
    {
        if (_forceClose || _isRestoringDisplayLayout || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _isDisplayLayoutRecoveryPending = true;
        if (_displayLayoutTimer is null)
        {
            // A one-shot debounce lets the OS and WPF finish a burst of mode/DPI changes.
            // There is no polling or background work while the display is unchanged.
            _displayLayoutTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _displayLayoutTimer.Tick += OnDisplayLayoutTimer;
        }
        _displayLayoutTimer.Stop();
        _displayLayoutTimer.Start();
    }

    private void OnDisplayLayoutTimer(object? sender, EventArgs e)
    {
        _displayLayoutTimer?.Stop();
        if (_forceClose || !_isVisibleBoundsClampingEnabled || PreferredWindowOriginPixels is null)
        {
            return;
        }
        if (_isMappingViewTransitioning || _isRollTransitioning || _isDrawerResizing || _isSurfaceDragging)
        {
            RequestDisplayLayoutRecovery();
            return;
        }
        if (!IsVisible)
        {
            // Show/ShowAll request a fresh repair after reloading hidden box contents.
            return;
        }

        _isDisplayLayoutRecoveryPending = false;
        _isRestoringDisplayLayout = true;
        try
        {
            var preferred = PreferredWindowOriginPixels!.Value;
            var dpiBeforeMove = GetDpiScale();
            var monitorPoint = new NativePoint
            {
                X = ToNativeCoordinate(preferred.X + WindowBorder.Margin.Left * dpiBeforeMove.DpiScaleX + 1),
                Y = ToNativeCoordinate(preferred.Y + WindowBorder.Margin.Top * dpiBeforeMove.DpiScaleY + 1)
            };
            if (!TryGetMonitorWorkArea(monitorPoint, out var targetWorkArea))
            {
                return;
            }
            // Move first so reconnecting a monitor also restores the window's correct DPI.
            MoveWindowOriginPixels(preferred.X, preferred.Y);
            RestoreContentSizingAfterDisplayChange(this, WindowBorder);
            var dpi = GetDpiScale();
            var workArea = GetWorkAreaPixels();
            if (workArea.IsEmpty)
            {
                workArea = new Rect(targetWorkArea.Left, targetWorkArea.Top,
                    targetWorkArea.Right - targetWorkArea.Left, targetWorkArea.Bottom - targetWorkArea.Top);
            }
            var origin = _displayLayout.ResolveOrigin(
                new Size(ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY),
                WindowBorder.Margin, dpi, workArea);
            if (origin is Point resolved)
            {
                MoveWindowOriginPixels(resolved.X, resolved.Y);
            }
            QueueSendToBottom();
        }
        catch (Exception exception)
        {
            ViewModel.Logger.Error(exception, "Failed to restore desktop box layout after display change.");
        }
        finally
        {
            _isRestoringDisplayLayout = false;
        }
    }

    internal static void RestoreContentSizingAfterDisplayChange(Window window, FrameworkElement content)
    {
        // Shell-driven resizing can leave a SizeToContent HWND with constrained dimensions.
        // The view model still owns grid, cover and panel sizes; remeasure those bindings.
        window.ClearValue(WidthProperty);
        window.ClearValue(HeightProperty);
        window.SizeToContent = SizeToContent.WidthAndHeight;
        content.InvalidateMeasure();
        window.InvalidateMeasure();
        window.UpdateLayout();
    }

    private void StopDisplayLayoutRecovery()
    {
        _displayLayoutTimer?.Stop();
        if (_displayLayoutTimer is not null)
        {
            _displayLayoutTimer.Tick -= OnDisplayLayoutTimer;
            _displayLayoutTimer = null;
        }
    }
}
