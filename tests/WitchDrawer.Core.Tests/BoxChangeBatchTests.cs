using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class BoxChangeBatchTests
{
    [Fact]
    public async Task Batch_CoalescesBackgroundCommitsAndNestedScopes()
    {
        var notifier = new BoxChangeNotifier();
        var events = new List<BoxContentChangedEventArgs>();
        notifier.ContentChanged += (_, change) => events.Add(change);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using (notifier.BeginBatch())
        {
            await Task.Run(() => notifier.Publish(first));
            using (notifier.BeginBatch())
                await Task.Run(() => notifier.Publish(first, second));
            Assert.Empty(events);
        }

        Assert.Equal(new[] { first, second }.Order(), Assert.Single(events).BoxIds.Order());
        notifier.Publish(first);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void FailedBatch_PublishesSuccessfullyCommittedChangesOnce()
    {
        var notifier = new BoxChangeNotifier();
        var events = new List<BoxContentChangedEventArgs>();
        notifier.ContentChanged += (_, change) => events.Add(change);
        var committed = Guid.NewGuid();

        Assert.Throws<IOException>((Action)(() =>
        {
            using var batch = notifier.BeginBatch();
            notifier.Publish(committed);
            throw new IOException("later import failed");
        }));

        Assert.Equal(new[] { committed }, Assert.Single(events).BoxIds);
    }

    [Fact]
    public async Task IndependentOperation_IsNotDelayedByAnotherAsyncBatch()
    {
        var notifier = new BoxChangeNotifier();
        var events = new System.Collections.Concurrent.ConcurrentQueue<BoxContentChangedEventArgs>();
        notifier.ContentChanged += (_, change) => events.Enqueue(change);
        var first = Guid.NewGuid();
        var independent = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batched = Task.Run(async () =>
        {
            using var batch = notifier.BeginBatch();
            notifier.Publish(first);
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        notifier.Publish(independent);
        Assert.Equal(new[] { independent }, Assert.Single(events).BoxIds);
        release.SetResult();
        await batched;
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void EmptyAndRepeatedDisposal_DoNotSendExtraNotifications()
    {
        var notifier = new BoxChangeNotifier();
        var events = 0;
        notifier.ContentChanged += (_, _) => events++;
        var empty = notifier.BeginBatch();
        empty.Dispose();
        var batch = notifier.BeginBatch();
        notifier.Publish(Guid.NewGuid());
        batch.Dispose();
        batch.Dispose();
        Assert.Equal(1, events);
    }
}
