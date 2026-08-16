using System.Windows;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 映射盒「详细功能」展开动画的辅助计算：四象限定位。
/// </summary>
public static class ExpandAnimationHelper
{
    /// <summary>
    /// 计算窗口放大后的新位置：以用户点击点为锚点，向空间充足的方向扩展。
    /// </summary>
    /// <param name="windowLeft">窗口当前 Left（屏幕坐标 DIP）</param>
    /// <param name="windowTop">窗口当前 Top（屏幕坐标 DIP）</param>
    /// <param name="clickPoint">用户点击位置（窗口内坐标）</param>
    /// <param name="currentSize">当前窗口尺寸</param>
    /// <param name="expandedSize">放大后窗口尺寸</param>
    /// <param name="workArea">屏幕工作区</param>
    /// <returns>放大后窗口的 Left/Top</returns>
    public static Point CalculateExpandedPosition(
        double windowLeft,
        double windowTop,
        Point clickPoint,
        Size currentSize,
        Size expandedSize,
        Rect workArea)
    {
        // 将点击点转换为屏幕坐标
        var screenClickPoint = new Point(
            windowLeft + clickPoint.X,
            windowTop + clickPoint.Y);

        // 计算四个象限的可用空间
        var spaceRight = workArea.Right - screenClickPoint.X;
        var spaceLeft = screenClickPoint.X - workArea.Left;
        var spaceBottom = workArea.Bottom - screenClickPoint.Y;
        var spaceTop = screenClickPoint.Y - workArea.Top;

        // 决定扩展方向（优先向右下，空间不足则向左上）
        var expandRight = spaceRight >= expandedSize.Width / 2 || spaceRight >= spaceLeft;
        var expandDown = spaceBottom >= expandedSize.Height / 2 || spaceBottom >= spaceTop;

        // 计算新窗口位置（保持点击点在窗口内的相对位置）
        var relativeX = currentSize.Width > 0 ? clickPoint.X / currentSize.Width : 0.5;
        var relativeY = currentSize.Height > 0 ? clickPoint.Y / currentSize.Height : 0.5;

        var newLeft = expandRight
            ? screenClickPoint.X - (expandedSize.Width * relativeX)
            : screenClickPoint.X - (expandedSize.Width * (1 - relativeX));

        var newTop = expandDown
            ? screenClickPoint.Y - (expandedSize.Height * relativeY)
            : screenClickPoint.Y - (expandedSize.Height * (1 - relativeY));

        // 边界钳制：窗口不超出工作区
        newLeft = Math.Max(workArea.Left, Math.Min(newLeft, workArea.Right - expandedSize.Width));
        newTop = Math.Max(workArea.Top, Math.Min(newTop, workArea.Bottom - expandedSize.Height));

        return new Point(newLeft, newTop);
    }

    /// <summary>
    /// 计算详细视图的放大后高度：行数 × 放大行高 + 边距，限制在工作区高度的 70% 以内。
    /// </summary>
    /// <param name="itemCount">项目数量</param>
    /// <param name="expandedRowHeight">放大后的行高</param>
    /// <param name="verticalPadding">上下边距之和</param>
    /// <param name="workAreaHeight">工作区高度</param>
    /// <returns>详细视图的目标高度</returns>
    public static double CalculateExpandedHeight(
        int itemCount,
        double expandedRowHeight,
        double verticalPadding,
        double workAreaHeight)
    {
        var contentHeight = (Math.Max(1, itemCount) * expandedRowHeight) + verticalPadding;
        var maxHeight = workAreaHeight * 0.7;
        return Math.Min(contentHeight, maxHeight);
    }
}
