using System.Windows;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定 <see cref="DesktopBoxWindow.GetVisibleBounds"/> 与
/// <see cref="DesktopBoxWindow.MoveToVisibleOrigin"/> 的互逆不变量：
/// 重叠消解（DesktopBoxManager.ResolveWindowOverlaps）在可视区域坐标系里计算，
/// 写回窗口位置时必须还原阴影留白 Margin，否则每次消解窗口都会漂移一圈。
/// </summary>
public sealed class DesktopBoxWindowVisibleBoundsTests
{
    [Theory]
    [InlineData(100, 200, 240, 180, 6)]
    [InlineData(0, 0, 120, 96, 14)]
    [InlineData(500.5, 300.25, 200, 150, 10)]
    public void WindowOrigin_RoundTripsThroughVisibleBounds(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        double uniformMargin)
    {
        var margin = new Thickness(uniformMargin);

        var visible = DesktopBoxWindow.ComputeVisibleBounds(
            windowLeft, windowTop, windowWidth, windowHeight, margin);
        var (roundTrippedLeft, roundTrippedTop) =
            DesktopBoxWindow.ComputeWindowOrigin(visible.Left, visible.Top, margin);

        Assert.Equal(windowLeft, roundTrippedLeft, precision: 6);
        Assert.Equal(windowTop, roundTrippedTop, precision: 6);
    }

    [Fact]
    public void WindowOrigin_HandlesAsymmetricMargins()
    {
        var margin = new Thickness(4, 8, 12, 16);

        var visible = DesktopBoxWindow.ComputeVisibleBounds(50, 60, 200, 100, margin);
        var (left, top) = DesktopBoxWindow.ComputeWindowOrigin(visible.Left, visible.Top, margin);

        Assert.Equal(50, left, precision: 6);
        Assert.Equal(60, top, precision: 6);
        Assert.Equal(200 - 4 - 12, visible.Width);
        Assert.Equal(100 - 8 - 16, visible.Height);
    }

    [Fact]
    public void VisibleBounds_ClampsNegativeContentSizeToZero()
    {
        var margin = new Thickness(10);

        var visible = DesktopBoxWindow.ComputeVisibleBounds(0, 0, 12, 8, margin);

        Assert.Equal(0, visible.Width);
        Assert.Equal(0, visible.Height);
    }
}
