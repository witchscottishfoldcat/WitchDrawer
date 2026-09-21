using WitchDrawer.App.Views;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    // 吸附对齐与对齐辅助线。

    private void HideGuides()
    {
        HideVerticalGuide();
        HideHorizontalGuide();
    }

    private void ShowVerticalGuide(double x, double yStart, double height)
    {
        if (_verticalGuide == null)
        {
            _verticalGuide = new GuideLineWindow(true);
        }
        if (!_verticalGuide.IsVisible)
        {
            _verticalGuide.Show();
        }
        _verticalGuide.UpdateLine(x, yStart, x, yStart + height);
    }

    private void HideVerticalGuide()
    {
        _verticalGuide?.Hide();
    }

    private void ShowHorizontalGuide(double y, double xStart, double width)
    {
        if (_horizontalGuide == null)
        {
            _horizontalGuide = new GuideLineWindow(false);
        }
        if (!_horizontalGuide.IsVisible)
        {
            _horizontalGuide.Show();
        }
        _horizontalGuide.UpdateLine(xStart, y, xStart + width, y);
    }

    private void HideHorizontalGuide()
    {
        _horizontalGuide?.Hide();
    }

    private void PerformSnappingAndAlignment(DesktopBoxWindow draggedWindow, bool applySnap = true)
    {
        var dpi = draggedWindow.GetDpiScale();
        var snapThreshold = 10.0 * dpi.DpiScaleX;
        var visualGap = 8.0 * dpi.DpiScaleX;

        var boundsA = draggedWindow.GetVisibleBoundsPixels();
        if (boundsA.IsEmpty)
        {
            HideGuides();
            return;
        }

        var otherBounds = new List<System.Windows.Rect>();
        foreach (var pair in _windows)
        {
            var otherWindow = pair.Value;
            if (otherWindow == draggedWindow || !otherWindow.IsVisible)
            {
                continue;
            }

            var boundsB = otherWindow.GetVisibleBoundsPixels();
            if (!boundsB.IsEmpty)
            {
                otherBounds.Add(boundsB);
            }
        }

        var snap = SnapGeometry.Evaluate(boundsA, otherBounds, snapThreshold, visualGap);

        if (applySnap)
        {
            if (snap.SnappedVisibleLeft.HasValue || snap.SnappedVisibleTop.HasValue)
            {
                draggedWindow.MoveToVisibleOriginPixels(
                    snap.SnappedVisibleLeft ?? boundsA.Left,
                    snap.SnappedVisibleTop ?? boundsA.Top);
            }
        }

        if (snap.VerticalGuideX.HasValue && snap.VerticalGuideYMax > snap.VerticalGuideYMin)
        {
            ShowVerticalGuide(snap.VerticalGuideX.Value, snap.VerticalGuideYMin, snap.VerticalGuideYMax - snap.VerticalGuideYMin);
        }
        else
        {
            HideVerticalGuide();
        }

        if (snap.HorizontalGuideY.HasValue && snap.HorizontalGuideXMax > snap.HorizontalGuideXMin)
        {
            ShowHorizontalGuide(snap.HorizontalGuideY.Value, snap.HorizontalGuideXMin, snap.HorizontalGuideXMax - snap.HorizontalGuideXMin);
        }
        else
        {
            HideHorizontalGuide();
        }
    }
}
