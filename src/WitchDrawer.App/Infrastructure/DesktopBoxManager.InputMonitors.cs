using System.Windows;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    // 全局鼠标监听、桌面空白双击与外部点击清除选中。

    /// <summary>
    /// 盒子带 WS_EX_NOACTIVATE：点击盒子不激活任何窗口，随后点击桌面时
    /// Window/Application.Deactivated 都不会触发，选中框（蓝框）会一直残留。
    /// 全局鼠标钩子在每次按键时命中测试光标下的窗口：命中点不在某个盒子的
    /// 窗口上，就清掉那个盒子的选中态。命中本盒子时保留——本盒子自己的鼠标
    /// 处理（点空白清空/点项目改选）会接着处理这次点击。
    /// </summary>
    internal static bool ShouldClearSelectionOnOutsideClick(nint clickedHandle, nint boxHandle) =>
        clickedHandle != boxHandle;

    private void OnGlobalMouseButtonDown(int screenX, int screenY)
    {
        if (_closing)
        {
            return;
        }

        var clickedHandle = GlobalMouseButtonMonitor.HitTestWindowHandle(screenX, screenY);
        foreach (var window in _windows.Values)
        {
            if (ShouldClearSelectionOnOutsideClick(clickedHandle, window.NativeHandle))
            {
                window.ClearSelectionFromOutside();
            }
        }
    }

    private void OnGlobalMouseButtonPressed(
        int screenX,
        int screenY,
        uint timestamp,
        GlobalMouseButton button)
    {
        if (_closing)
        {
            return;
        }

        _desktopMouseButtonEvents.Writer.TryWrite(new DesktopMouseButtonEvent(
            screenX,
            screenY,
            timestamp,
            button,
            _isDesktopDoubleClickEnabled()));
    }

    private async Task ProcessDesktopMouseButtonEventsAsync()
    {
        await foreach (var mouseEvent in _desktopMouseButtonEvents.Reader.ReadAllAsync()
                           .ConfigureAwait(false))
        {
            if (_closing || !mouseEvent.IsDesktopDoubleClickEnabled)
            {
                _desktopDoubleClickDetector.Reset();
                continue;
            }

            bool isBlankDesktopPoint;
            try
            {
                isBlankDesktopPoint = mouseEvent.Button == GlobalMouseButton.Left
                    && DesktopIconVisibility.IsBlankDesktopPoint(
                        mouseEvent.ScreenX,
                        mouseEvent.ScreenY);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to test the desktop click target.");
                _desktopDoubleClickDetector.Reset();
                continue;
            }

            if (!_desktopDoubleClickDetector.RegisterButtonDown(
                    mouseEvent.ScreenX,
                    mouseEvent.ScreenY,
                    mouseEvent.Timestamp,
                    mouseEvent.Button,
                    isBlankDesktopPoint))
            {
                continue;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                continue;
            }

            _ = dispatcher.BeginInvoke(() =>
            {
                if (!_closing && _isDesktopDoubleClickEnabled())
                {
                    DesktopBackgroundDoubleClicked?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }
}
