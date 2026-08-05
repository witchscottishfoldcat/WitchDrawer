using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxManagerLayeringTests
{
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
}
