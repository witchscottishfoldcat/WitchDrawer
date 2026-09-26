using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WitchDrawer.Native.Windows;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

/// <summary>
/// 抽屉封面 / 映射列表 / 待办面板三组 Thumb resize 手势（游标起点 + DPI 换算 + 完成持久化）。
/// </summary>
public partial class DesktopBoxWindow
{
    private void OnDrawerResizeStarted(object sender, DragStartedEventArgs e)
    {
        _isDrawerResizing = true;
        _drawerResizeStartWidth = ViewModel.DrawerCoverWidth;
        _drawerResizeStartHeight = ViewModel.DrawerCoverHeight;
        ViewModel.BeginDrawerCoverResize();
        GetCursorPos(out _drawerResizeStartCursor);
        e.Handled = true;
    }

    private void OnDrawerResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetCursorPos(out var currentCursor))
        {
            return;
        }

        var horizontalDelta = currentCursor.X - _drawerResizeStartCursor.X;
        var verticalDelta = currentCursor.Y - _drawerResizeStartCursor.Y;
        var dpi = VisualTreeHelper.GetDpi(this);
        ViewModel.PreviewDrawerCoverResize(
            _drawerResizeStartWidth + (horizontalDelta / Math.Max(0.1, dpi.DpiScaleX)),
            _drawerResizeStartHeight + (verticalDelta / Math.Max(0.1, dpi.DpiScaleY)));
        e.Handled = true;
    }

    private async void OnDrawerResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDrawerResizing = false;
        if (e.Canceled)
        {
            // 拖拽被取消（如捕获丢失/Alt+Tab 切走）：回滚到拖拽前的尺寸，不保存。
            ViewModel.EndDrawerCoverResize(commit: false);
            QueueVisibleBoundsClamp();
            e.Handled = true;
            return;
        }

        ViewModel.EndDrawerCoverResize(commit: true);
        QueueVisibleBoundsClamp();
        try
        {
            await ViewModel.SaveDrawerCoverSizeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.ResizeDrawerCover(
                _drawerResizeStartWidth,
                _drawerResizeStartHeight);
            _ = exception;
        }

        e.Handled = true;
    }

    private void OnMappingListResizeStarted(object sender, DragStartedEventArgs e)
    {
        _mappingListResizeStartWidth = ViewModel.MappingListWidth;
        GetCursorPos(out _mappingListResizeStartCursor);
        e.Handled = true;
    }

    private void OnMappingListResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetCursorPos(out var currentCursor))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var horizontalDelta = currentCursor.X - _mappingListResizeStartCursor.X;
        ViewModel.ResizeMappingListWidth(
            _mappingListResizeStartWidth
            + (horizontalDelta / Math.Max(0.1, dpi.DpiScaleX)));
        e.Handled = true;
    }

    private async void OnMappingListResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            ViewModel.ResizeMappingListWidth(_mappingListResizeStartWidth);
            e.Handled = true;
            return;
        }

        await ViewModel.SaveMappingListWidthAsync();
        e.Handled = true;
    }

    private void OnTodoResizeStarted(object sender, DragStartedEventArgs e)
    {
        _todoResizeStartWidth = ViewModel.TodoPanelWidth;
        _todoResizeStartHeight = ViewModel.TodoPanelHeight;
        GetCursorPos(out _todoResizeStartCursor);
        e.Handled = true;
    }

    private void OnTodoResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string edge } || !GetCursorPos(out var currentCursor))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var horizontalDelta = (currentCursor.X - _todoResizeStartCursor.X)
            / Math.Max(0.1, dpi.DpiScaleX);
        var verticalDelta = (currentCursor.Y - _todoResizeStartCursor.Y)
            / Math.Max(0.1, dpi.DpiScaleY);
        var width = edge is "Right" or "Corner"
            ? _todoResizeStartWidth + horizontalDelta
            : _todoResizeStartWidth;
        var height = edge is "Bottom" or "Corner"
            ? _todoResizeStartHeight + verticalDelta
            : _todoResizeStartHeight;
        ViewModel.ResizeTodoPanel(width, height);
        e.Handled = true;
    }

    private async void OnTodoResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            ViewModel.ResizeTodoPanel(_todoResizeStartWidth, _todoResizeStartHeight);
            e.Handled = true;
            return;
        }

        if (!await ViewModel.SaveTodoPanelSizeAsync())
        {
            ViewModel.ResizeTodoPanel(_todoResizeStartWidth, _todoResizeStartHeight);
        }

        e.Handled = true;
    }
}
