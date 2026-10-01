using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Tests;

public sealed class BoxContentSyncCoordinatorTests
{
    [Fact]
    public async Task RequestsBeforeDispatch_MergeDistinctBoxesIntoOneRefresh()
    {
        var queued = new Queue<Action>();
        var requests = new List<BoxRefreshRequest>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var coordinator = new BoxContentSyncCoordinator(new BoxChangeNotifier(),
            [new Target(request => { requests.Add(request); return Task.CompletedTask; })],
            queued.Enqueue, NullAppLogger.Instance);

        coordinator.Request(new([first]));
        coordinator.Request(new([second, first]));
        Assert.False(coordinator.WhenIdleAsync().IsCompleted);
        Assert.Single(queued)();
        await coordinator.WhenIdleAsync();

        var request = Assert.Single(requests);
        Assert.Equal(new[] { first, second }, request.BoxIds);
    }

    [Fact]
    public async Task ChangeDuringRefresh_IsDeliveredInNextPass()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<BoxRefreshRequest>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var coordinator = new BoxContentSyncCoordinator(new BoxChangeNotifier(),
            [new Target(async request =>
            {
                requests.Add(request);
                if (requests.Count == 1) { entered.SetResult(); await release.Task; }
            })], action => action(), NullAppLogger.Instance);

        coordinator.Request(new([first]));
        await entered.Task;
        coordinator.Request(new([second]));
        release.SetResult();
        await coordinator.WhenIdleAsync();

        Assert.Equal(2, requests.Count);
        Assert.Equal(new[] { second }, requests[1].BoxIds);
    }

    [Fact]
    public async Task FailingSurface_DoesNotPreventOtherSurfacesFromRefreshing()
    {
        var refreshed = 0;
        using var coordinator = new BoxContentSyncCoordinator(new BoxChangeNotifier(),
            [new Target(_ => throw new InvalidOperationException("closed view")),
             new Target(_ => { refreshed++; return Task.CompletedTask; })],
            action => action(), NullAppLogger.Instance);

        coordinator.Request(BoxRefreshRequest.All);
        await coordinator.WhenIdleAsync();

        Assert.Equal(1, refreshed);
    }

    [Fact]
    public async Task FullRefreshSupersedesBoxRequests_AndDisposeCancelsQueuedWork()
    {
        var queued = new Queue<Action>();
        var requests = new List<BoxRefreshRequest>();
        var coordinator = new BoxContentSyncCoordinator(new BoxChangeNotifier(),
            [new Target(request => { requests.Add(request); return Task.CompletedTask; })],
            queued.Enqueue, NullAppLogger.Instance);
        coordinator.Request(new([Guid.NewGuid()]));
        coordinator.Request(BoxRefreshRequest.All);
        queued.Dequeue()();
        await coordinator.WhenIdleAsync();
        Assert.Null(Assert.Single(requests).BoxIds);

        coordinator.Request(new([Guid.NewGuid()]));
        coordinator.Dispose();
        queued.Dequeue()();
        await coordinator.WhenIdleAsync();
        Assert.Single(requests);
    }

    private sealed class Target(Func<BoxRefreshRequest, Task> refresh) : IBoxContentRefreshTarget
    {
        public Task RefreshContentAsync(BoxRefreshRequest request) => refresh(request);
    }
}
