using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class StartupBoxRefreshCoordinatorTests
{
    [Fact]
    public void InitialLoadEventIsIgnoredAndChangesDuringDesktopRefreshAreReplayed()
    {
        var coordinator = new StartupBoxRefreshCoordinator();

        Assert.False(coordinator.ShouldRefreshNow());
        Assert.False(coordinator.HasPendingRefresh);
        coordinator.MarkMainViewModelLoaded();
        Assert.False(coordinator.ShouldRefreshNow());
        Assert.True(coordinator.HasPendingRefresh);
        Assert.True(coordinator.CompleteInitialRefresh());
        Assert.True(coordinator.ShouldRefreshNow());
    }

    [Fact]
    public void NoChangesDuringDesktopRefreshNeedsNoReplay()
    {
        var coordinator = new StartupBoxRefreshCoordinator();

        coordinator.MarkMainViewModelLoaded();

        Assert.False(coordinator.CompleteInitialRefresh());
        Assert.True(coordinator.ShouldRefreshNow());
    }
}
