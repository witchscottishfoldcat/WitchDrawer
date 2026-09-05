using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxManagerLayeringTests
{
    [Theory]
    [InlineData(0x0000u, true)]
    [InlineData(0x0003u, true)]
    [InlineData(0x0004u, false)]
    [InlineData(0x0017u, false)]
    [InlineData(0x0044u, true)]
    [InlineData(0x0084u, false)]
    public void WindowPositionChange_OnlyReordersOrShowsNeedRepair(uint flags, bool expected)
    {
        Assert.Equal(expected, DesktopToolWindow.AffectsDesktopLayer(flags));
    }

    [Theory]
    [InlineData(DesktopToolWindow.SizeMessage, 1, true)]
    [InlineData(DesktopToolWindow.SizeMessage, 0, false)]
    [InlineData(DesktopToolWindow.SizeMessage, 2, false)]
    [InlineData(DesktopToolWindow.WindowPositionChangedMessage, 0, false)]
    [InlineData(0x000F, 1, false)]
    public void NativeMessage_HandlesDirectMinimizeButNotResizeOrPaint(int message, int parameter, bool expected)
    {
        Assert.Equal(expected, DesktopToolWindow.IsDesktopLayerChangeMessage(message, parameter, nint.Zero));
    }
}
