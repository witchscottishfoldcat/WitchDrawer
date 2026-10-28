using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class DesktopListViewHitTesterTests
{
    [Fact]
    public async Task TimeoutKeepsTheInUseSessionAliveAndDoesNotQueueMoreQueries()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        var releases = 0;
        var tester = new DesktopListViewHitTester(_ =>
        {
            Interlocked.Increment(ref opens);
            return new Session(() =>
            {
                Assert.True(Thread.CurrentThread.IsThreadPoolThread);
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(0, Volatile.Read(ref releases));
                return true;
            }, () =>
            {
                Interlocked.Increment(ref releases);
                disposed.TrySetResult();
            });
        }, TimeSpan.FromMilliseconds(20));

        try
        {
            var query = tester.IsBlankAsync(1, 100, 200);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await query.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, Volatile.Read(ref releases));
            for (var index = 0; index < 100; index++)
                Assert.False(await tester.IsBlankAsync(1, 100, 200));
            Assert.Equal(1, Volatile.Read(ref opens));
        }
        finally { release.Set(); }

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref releases));
    }

    [Fact]
    public async Task CompletedQueriesReleaseTheirSessionsAndAllowTheNextRequest()
    {
        var disposals = 0;
        var tester = new DesktopListViewHitTester(_ => new Session(() => true,
            () => Interlocked.Increment(ref disposals)), TimeSpan.FromSeconds(5));

        Assert.True(await tester.IsBlankAsync(1, 10, 20));
        Assert.True(await tester.IsBlankAsync(1, 30, 40));
        Assert.Equal(2, disposals);
    }

    [Fact]
    public async Task ProviderFailureReleasesItsSessionAndTreatsThePointAsUnknown()
    {
        var disposals = 0;
        var tester = new DesktopListViewHitTester(_ => new Session(
            () => throw new InvalidOperationException("Explorer restarted"),
            () => Interlocked.Increment(ref disposals)), TimeSpan.FromSeconds(5));

        Assert.False(await tester.IsBlankAsync(1, 10, 20));
        Assert.Equal(1, disposals);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(null, false)]
    [InlineData("0", false)]
    public void AccessibilityHitTestRecognizesOnlyChildIdSelfAsBlank(object? result, bool expected)
        => Assert.Equal(expected, AccessibleDesktopHitTest.IsBlankResult(result));

    private sealed class Session(Func<bool> hitTest, Action dispose) : IDesktopHitTestSession
    {
        public bool IsBlankAt(int screenX, int screenY) => hitTest();
        public void Dispose() => dispose();
    }
}
