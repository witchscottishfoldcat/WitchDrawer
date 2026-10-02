using System.Windows;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DesktopLayerUpdateQueueTests
{
    [Fact]
    public void WorkerThreadBurst_IsCoalescedAndRunsOnDispatcher() => RunSta(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var calls = 0;
        using var queue = new DesktopLayerUpdateQueue(dispatcher, () =>
        {
            dispatcher.VerifyAccess();
            calls++;
        }, exception => throw new InvalidOperationException("Unexpected queue error", exception));
        Task.Run(() => Parallel.For(0, 100, _ => queue.Request())).GetAwaiter().GetResult();
        Assert.Equal(0, calls);
        Pump();
        Assert.Equal(1, calls);
        // No event means no repeat. A later event is still processed.
        Pump();
        Assert.Equal(1, calls);
        queue.Request();
        Pump();
        Assert.Equal(2, calls);
    });

    [Fact]
    public void SelfNotifications_DoNotQueueAnotherRepair() => RunSta(() =>
    {
        var calls = 0;
        DesktopLayerUpdateQueue? queue = null;
        queue = new DesktopLayerUpdateQueue(Dispatcher.CurrentDispatcher, () =>
        {
            calls++;
            queue!.Request();
        }, exception => throw exception);
        using (queue)
        {
            queue.Request();
            Pump();
            Pump();
            Assert.Equal(1, calls);
        }
    });

    [Fact]
    public void Dispose_SuppressesPendingAndFutureWork() => RunSta(() =>
    {
        var calls = 0;
        var queue = new DesktopLayerUpdateQueue(Dispatcher.CurrentDispatcher, () => calls++, _ => { });
        queue.Request();
        queue.Dispose();
        queue.Dispose();
        Task.Run(queue.Request).GetAwaiter().GetResult();
        Pump();
        Assert.Equal(0, calls);
    });

    [Fact]
    public void FailedRepair_IsReportedAndDoesNotDisableNextEvent() => RunSta(() =>
    {
        var calls = 0;
        var errors = new List<Exception>();
        using var queue = new DesktopLayerUpdateQueue(Dispatcher.CurrentDispatcher, () =>
        {
            if (++calls == 1) throw new InvalidOperationException("Expected test failure");
        }, errors.Add);
        queue.Request();
        Pump();
        Assert.Single(errors);
        queue.Request();
        Pump();
        Assert.Equal(2, calls);
    });

    [Fact]
    public void Snapshot_ExcludesDeliberatelyHiddenClosedAndCollapsedWindows()
    {
        var windows = new[]
        {
            (Visibility.Visible, true), (Visibility.Hidden, true),
            (Visibility.Collapsed, true), (Visibility.Visible, false)
        };
        var result = DesktopBoxManager.SnapshotDesktopLayerWindows(windows, w => w.Item1, w => w.Item2);
        Assert.Equal(windows[0], Assert.Single(result));
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    internal static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Dispatcher test timed out.");
        Assert.Null(failure);
    }
}
