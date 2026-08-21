using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class GuideLineWindowTests
{
    [Theory]
    [InlineData(true, 2860, 120, 2860, 780, 2858, 120, 4, 660)]
    [InlineData(false, 180, 1680, 920, 1680, 180, 1678, 740, 4)]
    [InlineData(true, -1420, 80, -1420, 600, -1422, 80, 4, 520)]
    public void OverlayBounds_PreservePhysicalCoordinatesAcrossMonitorOrigins(
        bool isVertical,
        double x1,
        double y1,
        double x2,
        double y2,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedHeight)
    {
        var bounds = GuideLineWindow.CalculateOverlayBounds(
            isVertical,
            x1,
            y1,
            x2,
            y2);

        Assert.Equal(expectedLeft, bounds.Left);
        Assert.Equal(expectedTop, bounds.Top);
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
    }
}
