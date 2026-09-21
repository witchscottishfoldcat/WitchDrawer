using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 盒子表面（主表面与抽屉封面）按下拖动移动窗口（DragMove）。
/// </summary>
public partial class DesktopBoxWindow
{
    private void OnDrawerSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isPositionLocked
            || e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is not DependencyObject source
            || FindVisualAncestor<Button>(source) is not null
            || FindVisualAncestor<Thumb>(source) is not null)
        {
            return;
        }

        e.Handled = true;
        try
        {
            DragMove();
            QueueSendToBottom();
            if (_positionChangedCallback is not null)
            {
                FireAndForget.Run(
                    _positionChangedCallback(ViewModel.BoxId),
                    ViewModel.Logger,
                    $"Failed to run position callback for box {ViewModel.BoxId:N}.");
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindVisualAncestor<Button>(source) is not null
                || FindVisualAncestor<TextBox>(source) is not null
                || FindVisualAncestor<Thumb>(source) is not null))
        {
            return;
        }

        if (TryGetDrawerItem(e.OriginalSource, out _))
        {
            return;
        }

        ClearItemSelection();

        if (_isPositionLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _isSurfaceDragging = true;
            try
            {
                DragMove();
                QueueSendToBottom();
                if (_positionChangedCallback is not null)
                {
                    FireAndForget.Run(
                    _positionChangedCallback(ViewModel.BoxId),
                    ViewModel.Logger,
                    $"Failed to run position callback for box {ViewModel.BoxId:N}.");
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _isSurfaceDragging = false;
                if (!IsMouseOver)
                {
                    ScheduleHoverRollUp();
                }
            }
        }
    }
}
