using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 抽屉二级弹窗的打开/关闭/定位与弹出动画接线。
/// </summary>
public partial class DesktopBoxWindow
{
    private void OnExpandDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncDrawerSecondaryFromItems();
        PrepareDrawerSecondaryPopupForOpen();
        if (sender is UIElement centerTarget)
        {
            ConfigureDrawerSecondaryPopupPlacement(centerTarget);
        }

        DrawerSecondaryPopup.IsOpen = true;
        ClearItemSelection();
        e.Handled = true;
    }

    private void PrepareDrawerSecondaryPopupForOpen()
    {
        _drawerPopupAnimation.Prepare();
    }

    internal static void PrepareDrawerPopupScaleForPlacement(ScaleTransform scale)
    {
        DrawerPopupAnimation.ResetScale(scale);
    }

    private void ConfigureDrawerSecondaryPopupPlacement(UIElement centerTarget)
    {
        var popupSize = new Size(
            ViewModel.DrawerSecondaryPanelWidth,
            ViewModel.DrawerSecondaryPanelHeight);
        var anchor = GetVisibleBounds();
        var occupiedBounds = Application.Current.Windows
            .OfType<DesktopBoxWindow>()
            .Where(window => window != this && window.IsVisible)
            .Select(window => window.GetVisibleBounds())
            .ToArray();
        var placement = DrawerPopupPlacementSelector.Select(
            anchor,
            popupSize,
            occupiedBounds,
            DrawerPopupGap,
            DrawerPopupCollisionPadding,
            SystemParameters.WorkArea);

        DrawerSecondaryPopup.HorizontalOffset = 0;
        DrawerSecondaryPopup.VerticalOffset = 0;
        if (placement == DrawerPopupPlacement.Center)
        {
            DrawerSecondaryPopup.PlacementTarget = centerTarget;
            DrawerSecondaryPopup.Placement = PlacementMode.Center;
            return;
        }

        // Keep the collision-aware side selected above. Relative Popup placement
        // can be flipped by WPF near a screen edge, potentially putting it back on
        // top of a neighboring box.
        var target = DrawerPopupPlacementSelector.GetCandidateBounds(
            placement,
            anchor,
            popupSize,
            DrawerPopupGap);
        DrawerSecondaryPopup.PlacementTarget = null;
        DrawerSecondaryPopup.Placement = PlacementMode.Absolute;
        DrawerSecondaryPopup.HorizontalOffset = target.Left;
        DrawerSecondaryPopup.VerticalOffset = target.Top;
    }

    private void OnDrawerSecondaryPopupOpened(object? sender, EventArgs e)
    {
        // 弹窗 HWND 属主是盒子窗口，盒子窗口属主是桌面壳。弹窗打开时 Windows 会把
        // 整条属主链提前。第一时间断开弹窗属主，再把所有盒子压回桌面层。
        DetachDrawerPopupOwner();
        QueueSendToBottomAll();
        // 沉底在 ApplicationIdle 还会补一次，而压主窗口沉底会把它的属子弹窗一起拖下去；
        // 置顶必须排在所有沉底调用之后，所以用 SystemIdle 优先级。
        Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () =>
        {
            if (DrawerSecondaryPopup.IsOpen)
            {
                BringDrawerPopupToFront();
            }
        });

        // Popup has measured its child and placed its HWND. Start before the next
        // render instead of rendering a hidden frame and scheduling at Loaded.
        _drawerPopupAnimation.Start(ViewModel.LayoutSettings.DrawerPrimaryIconFrameSize);
    }

    private void OnDrawerSecondaryPopupClosed(object? sender, EventArgs e) =>
        _drawerPopupAnimation.Stop();

    private void OnCollapseDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDrawerExpanded = false;
        ClearItemSelection();
        e.Handled = true;
    }
}
