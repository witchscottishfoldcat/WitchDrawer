using WitchDrawer.Native.Files;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class ShellChangeNotifierTests
{
    [Theory]
    [InlineData(ItemKind.File, "report.txt", 0x00000004u)]
    [InlineData(ItemKind.File, "shortcut.lnk", 0x00000004u)]
    [InlineData(ItemKind.Directory, "folder", 0x00000010u)]
    public async Task ImportedItem_NotifiesSourceRemovalThenParentRefresh(
        ItemKind kind, string name, uint expectedEvent)
    {
        var item = CreateItem(kind, name);
        var events = new List<(uint Event, uint Flags, string Path)>();

        await ShellChangeNotifier.NotifyItemImportedAsync(item, NullAppLogger.Instance,
            (eventId, flags, path) => events.Add((eventId, flags, path)));

        Assert.Equal(new[]
        {
            (expectedEvent, 0x2005u, item.SourcePath!),
            (0x00001000u, 0x2005u, System.IO.Path.GetDirectoryName(item.SourcePath!)!)
        }, events);
    }

    [Fact]
    public async Task ImportedMappingReference_DoesNotNotifyRemoval()
    {
        var item = CreateItem(ItemKind.File, "keep.txt") with { StoredPath = null };
        var notified = false;

        await ShellChangeNotifier.NotifyItemImportedAsync(item, NullAppLogger.Instance,
            (_, _, _) => notified = true);

        Assert.False(notified);
    }

    [Fact]
    public async Task ImportedItem_RefreshFailureDoesNotFailSuccessfulImport()
    {
        var logger = new RecordingLogger();
        var failure = new InvalidOperationException("Shell unavailable");

        await ShellChangeNotifier.NotifyItemImportedAsync(CreateItem(ItemKind.File, "done.txt"), logger,
            (_, _, _) => throw failure);

        Assert.Same(failure, logger.ErrorException);
    }

    [Fact]
    public async Task ImportedItem_SlowShellDoesNotBlockCaller()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() => returned.SetResult(ShellChangeNotifier.NotifyItemImportedAsync(
            CreateItem(ItemKind.File, "slow.txt"), NullAppLogger.Instance, (_, _, _) =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            }))) { IsBackground = true }.Start();

        var returnedWhileShellBusy = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            returnedWhileShellBusy = await Task.WhenAny(returned.Task, Task.Delay(1000)) == returned.Task;
        }
        finally
        {
            release.Set();
        }

        await await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(returnedWhileShellBusy);
    }

    private static DrawerItem CreateItem(ItemKind kind, string name)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WitchDrawerShellTests");
        return new DrawerItem(Guid.NewGuid(), Guid.NewGuid(), name, kind,
            System.IO.Path.Combine(root, "source", name), System.IO.Path.Combine(root, "stored", name),
            0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public Exception? ErrorException { get; private set; }
        public void Info(string message) { }
        public void Error(Exception exception, string message) => ErrorException = exception;
    }

    [Theory]
    [InlineData(true, 0x00000008u)]   // directory -> SHCNE_MKDIR
    [InlineData(false, 0x00000002u)]  // file -> SHCNE_CREATE
    public void GetCreateEvent_MapsDirectoryFlag(bool isDirectory, uint expectedEvent)
    {
        Assert.Equal(expectedEvent, ShellChangeNotifier.GetCreateEvent(isDirectory));
    }
}
