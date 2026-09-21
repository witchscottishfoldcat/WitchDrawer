using System.Windows;
using System.Windows.Media;
using WitchDrawer.Native.Windows;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

/// <summary>
/// 视觉树遍历与屏幕命中测试辅助（含多个带测试的纯静态函数）。
/// </summary>
public partial class DesktopBoxWindow
{
    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private bool IsCursorOverOpenDrawerPopup()
    {
        // Popups are not part of Application.Current.Windows, so the window hit-test above
        // misses releases over the secondary drawer popup. Treat those as inside the app;
        // otherwise a short drag ending on the popup would wrongly move the item to the desktop.
        if (!DrawerSecondaryPopup.IsOpen
            || !DrawerSecondaryPopupRoot.IsVisible
            || !GetCursorPos(out var cursor))
        {
            return false;
        }

        try
        {
            var topLeft = DrawerSecondaryPopupRoot.PointToScreen(new Point(0, 0));
            var bottomRight = DrawerSecondaryPopupRoot.PointToScreen(
                new Point(DrawerSecondaryPopupRoot.ActualWidth, DrawerSecondaryPopupRoot.ActualHeight));
            return IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight);
        }
        catch (InvalidOperationException)
        {
            // Popup content has no presentation source yet; skip it.
            return false;
        }
    }

    internal static bool IsScreenPointInside(int x, int y, Point topLeft, Point bottomRight)
    {
        return x >= topLeft.X
            && x <= bottomRight.X
            && y >= topLeft.Y
            && y <= bottomRight.Y;
    }

    internal static bool IsSameOrVisualDescendant(Visual root, Visual candidate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidate);
        return ReferenceEquals(root, candidate) || root.IsAncestorOf(candidate);
    }

    private static bool IsCursorOverWitchDrawerWindow()
    {
        // Mouse.GetPosition is stale right after DoDragDrop; use the real cursor screen
        // position and compare against each window's on-screen rectangle.
        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = window.PointToScreen(new Point(0, 0));
                var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
                if (IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Window has no presentation source yet; skip it.
            }
        }

        return false;
    }
}
