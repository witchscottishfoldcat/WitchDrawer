using System.Windows;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 吸附对齐的纯几何计算：输入拖动盒与其他盒的可视矩形，输出吸附目标与辅助线区间。
/// 无任何 UI 副作用，10 分支匹配逻辑与原 PerformSnappingAndAlignment 内联实现逐字一致。
/// </summary>
internal readonly record struct SnapAlignment(
    double? SnappedVisibleLeft,
    double? SnappedVisibleTop,
    double? VerticalGuideX,
    double VerticalGuideYMin,
    double VerticalGuideYMax,
    double? HorizontalGuideY,
    double HorizontalGuideXMin,
    double HorizontalGuideXMax);

internal static class SnapGeometry
{
    internal static SnapAlignment Evaluate(
        Rect draggedBounds,
        IReadOnlyList<Rect> otherBounds,
        double snapThreshold,
        double visualGap)
    {
        double currentLeft = draggedBounds.Left;
        double currentTop = draggedBounds.Top;
        double width = draggedBounds.Width;
        double height = draggedBounds.Height;
        double rightA = draggedBounds.Right;
        double bottomA = draggedBounds.Bottom;
        double hCenterA = currentLeft + width / 2.0;
        double vCenterA = currentTop + height / 2.0;
        double? bestSnappedVisibleLeft = null;
        double? bestSnappedVisibleTop = null;

        double? verticalGuideX = null;
        double verticalGuideYMin = double.MaxValue;
        double verticalGuideYMax = double.MinValue;

        double? horizontalGuideY = null;
        double horizontalGuideXMin = double.MaxValue;
        double horizontalGuideXMax = double.MinValue;

        foreach (var boundsB in otherBounds)
        {
            double leftB = boundsB.Left;
            double topB = boundsB.Top;
            double widthB = boundsB.Width;
            double heightB = boundsB.Height;
            double rightB = boundsB.Right;
            double bottomB = boundsB.Bottom;
            double hCenterB = leftB + widthB / 2.0;
            double vCenterB = topB + heightB / 2.0;

            // 1. Vertical snapping
            if (Math.Abs(currentLeft - leftB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB;
                verticalGuideX = leftB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - rightB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB - width;
                verticalGuideX = rightB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(currentLeft - (rightB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB + visualGap;
                verticalGuideX = rightB + visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - (leftB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB - visualGap - width;
                verticalGuideX = leftB - visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(hCenterA - hCenterB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = hCenterB - width / 2.0;
                verticalGuideX = hCenterB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }

            // 2. Horizontal snapping
            if (Math.Abs(currentTop - topB) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB;
                horizontalGuideY = topB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - bottomB) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB - height;
                horizontalGuideY = bottomB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(currentTop - (bottomB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB + visualGap;
                horizontalGuideY = bottomB + visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - (topB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB - visualGap - height;
                horizontalGuideY = topB - visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(vCenterA - vCenterB) <= snapThreshold)
            {
                bestSnappedVisibleTop = vCenterB - height / 2.0;
                horizontalGuideY = vCenterB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
        }

        return new SnapAlignment(
            bestSnappedVisibleLeft,
            bestSnappedVisibleTop,
            verticalGuideX,
            verticalGuideYMin,
            verticalGuideYMax,
            horizontalGuideY,
            horizontalGuideXMin,
            horizontalGuideXMax);
    }
}
