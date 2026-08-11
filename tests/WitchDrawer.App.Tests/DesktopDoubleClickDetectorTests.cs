using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopDoubleClickDetectorTests
{
    [Fact]
    public void RegisterClick_TogglesOnlyForNearbyDesktopClicksWithinSystemDelay()
    {
        var detector = new DesktopDoubleClickDetector(500, 8, 8);

        Assert.False(detector.RegisterClick(100, 100, 1_000, isDesktopBackground: true));
        Assert.True(detector.RegisterClick(104, 96, 1_500, isDesktopBackground: true));
        Assert.False(detector.RegisterClick(104, 96, 1_501, isDesktopBackground: true));
    }

    [Theory]
    [InlineData(105, 100, 1_100)]
    [InlineData(100, 105, 1_100)]
    [InlineData(100, 100, 1_501)]
    public void RegisterClick_DoesNotToggleOutsideSystemThresholds(int x, int y, uint timestamp)
    {
        var detector = new DesktopDoubleClickDetector(500, 8, 8);

        Assert.False(detector.RegisterClick(100, 100, 1_000, isDesktopBackground: true));
        Assert.False(detector.RegisterClick(x, y, timestamp, isDesktopBackground: true));
    }

    [Fact]
    public void RegisterClick_NonDesktopClickResetsPendingDoubleClick()
    {
        var detector = new DesktopDoubleClickDetector(500, 8, 8);

        Assert.False(detector.RegisterClick(100, 100, 1_000, isDesktopBackground: true));
        Assert.False(detector.RegisterClick(100, 100, 1_100, isDesktopBackground: false));
        Assert.False(detector.RegisterClick(100, 100, 1_200, isDesktopBackground: true));
    }
}
