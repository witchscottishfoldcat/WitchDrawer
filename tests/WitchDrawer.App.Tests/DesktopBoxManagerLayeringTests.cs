using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxManagerLayeringTests
{
    [Theory]
    [InlineData(true, 100, 200, true)]
    [InlineData(false, 100, 200, false)]
    [InlineData(true, 201, 200, false)]
    [InlineData(true, 100, 0, false)]
    public void ShouldLowerMainWindowForShowDesktop_RequiresRecentShortcutAndDesktop(
        bool desktopIsForeground,
        long currentTick,
        long shortcutObservedUntilTick,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxManager.ShouldLowerMainWindowForShowDesktop(
                desktopIsForeground,
                currentTick,
                shortcutObservedUntilTick));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ResolveDesktopForegroundState_OnlyKeepsDesktopWindowsRaised(
        bool isDesktopWindow,
        bool isDesktopBoxWindow,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxManager.ResolveDesktopForegroundState(
                isDesktopWindow,
                isDesktopBoxWindow));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldSendToBottom_DoesNotRewriteZOrderDuringShowDesktop(
        bool isDesktopForeground,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxWindow.ShouldSendToBottom(isDesktopForeground));
    }
}
