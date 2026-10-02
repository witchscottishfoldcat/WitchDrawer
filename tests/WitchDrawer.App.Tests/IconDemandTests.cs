using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class IconDemandTests
{
    [Fact]
    public async Task CancelledQueuedRequests_DoNotExtractOrDelayTheNextVisibleRequest()
    {
        using var release = new ManualResetEventSlim();
        using var started = new CountdownEvent(4);
        var extracted = new ConcurrentQueue<string>();
        var active = 0;
        var peakActive = 0;
        var cache = new ShellIconCache((path, _, _) =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref peakActive, current);
            extracted.Enqueue(System.IO.Path.GetFileName(path));
            try
            {
                if (System.IO.Path.GetFileName(path).StartsWith("block-", StringComparison.Ordinal))
                {
                    started.Signal();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                }

                return CreateIcon();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var blockers = Enumerable.Range(0, 4)
            .Select(index => cache.GetIconAsync($"block-{index}.txt", false, 32)).ToArray();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            using var cancelled = new CancellationTokenSource();
            var stale = Enumerable.Range(0, 128)
                .Select(index => cache.GetIconAsync($"stale-{index}.txt", false, 32, cancelled.Token))
                .ToArray();
            cancelled.Cancel();
            foreach (var task in stale)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }

            var visible = cache.GetIconAsync("visible.txt", false, 32);
            release.Set();
            await Task.WhenAll(blockers.Append(visible)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.DoesNotContain(extracted, path => path.StartsWith("stale-", StringComparison.Ordinal));
            Assert.Contains("visible.txt", extracted);
            Assert.Equal(4, peakActive);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(blockers).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task CancellingOneSharedQueuedConsumer_PreservesTheOtherAndCachesFrozenResult()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedExtractions = 0;
        var cache = new ShellIconCache((path, _, _) =>
        {
            if (System.IO.Path.GetFileName(path) == "block.txt")
            {
                started.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            else
            {
                Interlocked.Increment(ref sharedExtractions);
            }

            return CreateIcon();
        }, maxConcurrentLoads: 1);

        var blocker = cache.GetIconAsync("block.txt", false, 32);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancelled = new CancellationTokenSource();
            var first = cache.GetIconAsync("shared.txt", false, 32, cancelled.Token);
            var second = cache.GetIconAsync("SHARED.txt", false, 32);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            release.Set();
            var icon = await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(icon);
            Assert.True(icon.IsFrozen);
            Assert.Same(icon, await cache.GetIconAsync("shared.txt", false, 32));
            Assert.Equal(1, sharedExtractions);
        }
        finally
        {
            release.Set();
            await blocker.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task EntirelyCancelledQueuedEntry_CanBeRequestedAgain()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extractions = 0;
        var cache = new ShellIconCache((path, _, _) =>
        {
            if (System.IO.Path.GetFileName(path) == "block.txt")
            {
                started.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            else
            {
                Interlocked.Increment(ref extractions);
            }

            return CreateIcon();
        }, maxConcurrentLoads: 1);
        var blocker = cache.GetIconAsync("block.txt", false, 32);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancelled = new CancellationTokenSource();
            var stale = cache.GetIconAsync("again.txt", false, 32, cancelled.Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
            var current = cache.GetIconAsync("again.txt", false, 32);
            release.Set();
            Assert.NotNull(await current.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, extractions);
        }
        finally
        {
            release.Set();
            await blocker.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task InFlightExtraction_RemainsSharedWhenItsFirstConsumerCancels()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extractions = 0;
        var cache = new ShellIconCache((_, _, _) =>
        {
            Interlocked.Increment(ref extractions);
            started.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return CreateIcon();
        });
        using var cancelled = new CancellationTokenSource();
        var first = cache.GetIconAsync("in-flight.txt", false, 32, cancelled.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            var second = cache.GetIconAsync("in-flight.txt", false, 32);
            release.Set();
            Assert.NotNull(await second.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, extractions);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CompletedCache_StillEvictsItsOldestEntryAtTheConfiguredLimit()
    {
        var calls = new ConcurrentDictionary<string, int>();
        var cache = new ShellIconCache((path, _, _) =>
        {
            calls.AddOrUpdate(System.IO.Path.GetFileName(path), 1, (_, count) => count + 1);
            return CreateIcon();
        }, maxCachedEntries: 2);
        await cache.GetIconAsync("a.txt", false, 32);
        await cache.GetIconAsync("b.txt", false, 32);
        await cache.GetIconAsync("c.txt", false, 32);
        await cache.GetIconAsync("a.txt", false, 32);
        Assert.Equal(2, calls["a.txt"]);
    }

    [Fact]
    public void ViewModel_DemandIsSharedAndSizeChangesOnlyRequestIconsWithActiveConsumers()
    {
        var requests = new List<(int Size, CancellationToken Token)>();
        var item = CreateItem("item.txt", (_, _, size, token) =>
        {
            requests.Add((size, token));
            return AwaitCancellationAsync(token);
        });

        item.RequestIconSize(48);
        Assert.Empty(requests);
        var first = item.AcquireIconDemand();
        var second = item.AcquireIconDemand();
        Assert.Single(requests);
        first.Dispose();
        Assert.False(requests[0].Token.IsCancellationRequested);
        second.Dispose();
        Assert.True(requests[0].Token.IsCancellationRequested);
        Assert.False(item.IsIconLoadRequested);

        item.RequestIconSize(64);
        item.ReloadIconIfNeeded();
        Assert.Single(requests);
        using var visibleAgain = item.AcquireIconDemand();
        Assert.Equal(64, requests[1].Size);
        item.RequestIconSize(96);
        Assert.True(requests[1].Token.IsCancellationRequested);
        Assert.Equal(96, requests[2].Size);
    }

    [Fact]
    public void ReturningToAnAlreadyLoadedSize_CancelsItsQueuedReplacement()
    {
        var icon = CreateIcon();
        icon.Freeze();
        var requests = new List<(int Size, CancellationToken Token)>();
        var item = CreateItem("size.txt", (_, _, size, token) =>
        {
            requests.Add((size, token));
            return size == 32 ? Task.FromResult<ImageSource?>(icon) : AwaitCancellationAsync(token);
        });
        using var visible = item.AcquireIconDemand();
        Assert.Same(icon, item.IconImage);
        item.RequestIconSize(64);
        item.RequestIconSize(32);
        Assert.True(requests[1].Token.IsCancellationRequested);
        Assert.Equal(2, requests.Count);
        Assert.Same(icon, item.IconImage);
    }

    [Fact]
    public async Task CancelledOrSupersededLoad_CannotPublishItsLateIcon()
    {
        var results = new List<TaskCompletionSource<ImageSource?>>();
        var item = CreateItem("late.txt", (_, _, _, _) =>
        {
            var result = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            results.Add(result);
            return result.Task;
        });
        var first = item.AcquireIconDemand();
        item.RequestIconSize(64);
        var oldIcon = CreateIcon();
        oldIcon.Freeze();
        results[0].SetResult(oldIcon);
        await Task.Delay(50);
        Assert.Null(item.IconImage);
        first.Dispose();
        results[1].SetResult(oldIcon);
        await Task.Delay(50);
        Assert.Null(item.IconImage);

        using var current = item.AcquireIconDemand();
        var currentIcon = CreateIcon();
        currentIcon.Freeze();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(item.IconImage)) applied.TrySetResult();
        };
        results[2].SetResult(currentIcon);
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(currentIcon, item.IconImage);
    }

    [Fact]
    public Task DeferredLoad_CollapsedPlaceholderRequestsWhileItsHostIsVisibleAndReleasesOnRecycling()
    {
        return RunOnStaAsync(async () =>
        {
            var first = CreateItem("first.txt", (_, _, _, token) => AwaitCancellationAsync(token));
            var second = CreateItem("second.txt", (_, _, _, token) => AwaitCancellationAsync(token));
            var image = new Image { Visibility = Visibility.Collapsed, DataContext = first };
            var host = new Grid();
            host.Children.Add(image);
            DeferredIconLoad.SetIsEnabled(image, true);
            var window = new Window
            {
                Content = host, Width = 50, Height = 50, Left = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(first.IsIconLoadRequested);
                image.DataContext = second;
                Assert.False(first.IsIconLoadRequested);
                Assert.True(second.IsIconLoadRequested);
                host.Visibility = Visibility.Collapsed;
                Assert.False(second.IsIconLoadRequested);
                host.Visibility = Visibility.Visible;
                Assert.True(second.IsIconLoadRequested);
                window.Hide();
                Assert.False(second.IsIconLoadRequested);
                window.Show();
                Assert.True(second.IsIconLoadRequested);
                window.Content = null;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(second.IsIconLoadRequested);
                window.Content = host;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(second.IsIconLoadRequested);
                DeferredIconLoad.SetIsEnabled(image, false);
                Assert.False(second.IsIconLoadRequested);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static DrawerItemViewModel CreateItem(
        string path,
        Func<string?, bool, int, CancellationToken, Task<ImageSource?>> loadIcon)
    {
        return new DrawerItemViewModel(
            new DrawerItem(Guid.NewGuid(), Guid.NewGuid(), path, ItemKind.File, path, null, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            null, false, 32, null, loadIcon);
    }

    private static ImageSource CreateIcon()
    {
        return new DrawingImage(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
    }

    private static async Task<ImageSource?> AwaitCancellationAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        return null;
    }

    private static Task RunOnStaAsync(Func<Task> test)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); finished.SetResult(); }
                catch (Exception exception) { finished.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static class InterlockedExtensions
    {
        internal static void Max(ref int location, int value)
        {
            var previous = Volatile.Read(ref location);
            while (previous < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, previous);
                if (observed == previous) return;
                previous = observed;
            }
        }
    }
}
