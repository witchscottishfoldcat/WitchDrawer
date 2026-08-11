using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopDoubleClickDetectorTests
{
    [Theory]
    [InlineData(0x0201, GlobalMouseButton.Left)]
    [InlineData(0x00A1, GlobalMouseButton.Left)]
    [InlineData(0x0204, GlobalMouseButton.Right)]
    [InlineData(0x00A4, GlobalMouseButton.Right)]
    [InlineData(0x0207, GlobalMouseButton.Middle)]
    [InlineData(0x00A7, GlobalMouseButton.Middle)]
    public void GetMouseButton_MapsSupportedButtonMessages(
        int message,
        GlobalMouseButton expected)
    {
        Assert.Equal(expected, GlobalMouseButtonMonitor.GetMouseButton(message));
    }

    [Fact]
    public void GetMouseButton_IgnoresUnsupportedMessages()
    {
        Assert.Null(GlobalMouseButtonMonitor.GetMouseButton(0x0200));
    }

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

    [Theory]
    [InlineData(GlobalMouseButton.Right)]
    [InlineData(GlobalMouseButton.Middle)]
    public void RegisterButtonDown_NonLeftButtonResetsPendingDoubleClick(GlobalMouseButton button)
    {
        var detector = new DesktopDoubleClickDetector(500, 8, 8);

        Assert.False(detector.RegisterButtonDown(
            100,
            100,
            1_000,
            GlobalMouseButton.Left,
            isDesktopBackground: true));
        Assert.False(detector.RegisterButtonDown(
            100,
            100,
            1_100,
            button,
            isDesktopBackground: false));
        Assert.False(detector.RegisterButtonDown(
            100,
            100,
            1_200,
            GlobalMouseButton.Left,
            isDesktopBackground: true));
    }
}
