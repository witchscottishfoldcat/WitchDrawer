using System.Windows;
using System.Windows.Controls.Primitives;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

internal static class BoxSettingsPopupPlacement
{
    internal static void Configure(Popup popup, FrameworkElement target, Size expandedSize)
    {
        var screenOrigin = target.PointToScreen(new Point());
        var point = new User32Interop.NativePoint
        {
            X = User32Interop.ToNativeCoordinate(screenOrigin.X),
            Y = User32Interop.ToNativeCoordinate(screenOrigin.Y)
        };
        Rect workArea;
        if (User32Interop.TryGetMonitorWorkArea(point, out var bounds))
        {
            workArea = new Rect(target.PointFromScreen(new Point(bounds.Left, bounds.Top)),
                target.PointFromScreen(new Point(bounds.Right, bounds.Bottom)));
        }
        else
        {
            var fallback = SystemParameters.WorkArea;
            var relativeOrigin = target.PointFromScreen(new Point(fallback.Left, fallback.Top));
            workArea = new Rect(relativeOrigin, fallback.Size);
        }

        var origin = SelectOrigin(target.RenderSize, expandedSize, workArea);
        var screenPoint = target.PointToScreen(origin);
        var deviceOrigin = new Point(screenPoint.X - screenOrigin.X, screenPoint.Y - screenOrigin.Y);
        // Keep this origin for the whole open session. Selection can change the
        // panel's height, but its top edge must not depend on the current height.
        // WPF's custom placement callback uses device pixels, while RenderSize
        // and PointFromScreen use layout units. Convert before returning it.
        popup.Placement = PlacementMode.Custom;
        popup.CustomPopupPlacementCallback = (_, _, _) =>
            [new CustomPopupPlacement(deviceOrigin, PopupPrimaryAxis.None)];
    }

    internal static Point SelectOrigin(Size targetSize, Size expandedSize, Rect workArea)
    {
        const double gap = 6;
        var left = Math.Clamp(targetSize.Width - expandedSize.Width,
            workArea.Left, Math.Max(workArea.Left, workArea.Right - expandedSize.Width));
        var top = targetSize.Height + gap;
        if (top + expandedSize.Height > workArea.Bottom)
        {
            var above = -expandedSize.Height - gap;
            top = above >= workArea.Top ? above
                : Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - expandedSize.Height));
        }
        return new Point(left, top);
    }
}
