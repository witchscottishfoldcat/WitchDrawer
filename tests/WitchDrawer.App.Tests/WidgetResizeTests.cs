using System.Windows;
using WitchDrawer.App.Plugins;

namespace WitchDrawer.App.Tests;

public sealed class WidgetResizeTests
{
    [Theory]
    [InlineData(4, 80, 40, 100, 200, 400, 160)]
    [InlineData(8, 80, 40, 100, 200, 320, 200)]
    [InlineData(1, 80, 40, 180, 200, 240, 160)]
    [InlineData(2, 80, 40, 100, 240, 320, 120)]
    [InlineData(3, 80, 40, 180, 240, 240, 120)]
    [InlineData(12, 80, 40, 100, 200, 400, 200)]
    [InlineData(9, 80, 40, 180, 200, 240, 200)]
    [InlineData(6, 80, 40, 100, 240, 400, 120)]
    public void EachEdgeKeepsOppositeEdgesFixed(int edges, double dx, double dy,
        double left, double top, double width, double height)
    {
        var bounds = WidgetWindow.ResizeBounds(new Rect(100, 200, 320, 160), (WidgetResizeEdges)edges, dx, dy,
            new Size(160, 92), new Size(1200, 800));
        Assert.Equal(new Rect(left, top, width, height), bounds);
    }

    [Fact]
    public void DraggingPastMinimumKeepsOppositeCornerAndNeverInvertsSize()
    {
        var bounds = WidgetWindow.ResizeBounds(new Rect(100, 200, 320, 160), WidgetResizeEdges.Left | WidgetResizeEdges.Top,
            1000, 1000, new Size(160, 92), new Size(1200, 800));
        Assert.Equal(new Rect(260, 268, 160, 92), bounds);
    }

    [Fact]
    public void EnlargingStopsAtMaximumWithoutMovingTopLeft()
    {
        var bounds = WidgetWindow.ResizeBounds(new Rect(100, 200, 320, 160), WidgetResizeEdges.Right | WidgetResizeEdges.Bottom,
            9000, 9000, new Size(160, 92), new Size(1200, 800));
        Assert.Equal(new Rect(100, 200, 1200, 800), bounds);
    }
}
