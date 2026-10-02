using WitchDrawer.Native.Files;

namespace WitchDrawer.Native.Tests;

public sealed class ShellFileLauncherTests
{
    [Fact]
    public async Task OpenAsync_BlockedShellOperation_ReturnsWithoutBlockingCallerAndUsesSta()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource<(int ThreadId, ApartmentState Apartment)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new ShellFileLauncher((_, _) =>
        {
            started.SetResult((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            release.Wait();
        });
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new Thread(() => returned.SetResult(launcher.OpenAsync("fixture.txt")))
        {
            IsBackground = true
        };
        caller.SetApartmentState(ApartmentState.STA);
        caller.Start();

        Task? operation = null;
        try
        {
            operation = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var worker = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(caller.ManagedThreadId, worker.ThreadId);
            Assert.Equal(ApartmentState.STA, worker.Apartment);
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            release.Set();
            await (operation ?? await returned.Task.WaitAsync(TimeSpan.FromSeconds(5)))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task OpenAsync_ShellFailure_PropagatesOriginalException()
    {
        var failure = new IOException("Shell fixture failed.");
        var launcher = new ShellFileLauncher((_, _) => throw failure);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => launcher.OpenAsync("fixture.txt")));
    }

    [Fact]
    public async Task OpenAsync_AlreadyCancelled_DoesNotStartShellOperation()
    {
        var started = false;
        var launcher = new ShellFileLauncher((_, _) => started = true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => launcher.OpenAsync("fixture.txt", new CancellationToken(canceled: true)));
        Assert.False(started);
    }

    [Fact]
    public async Task OpenAsync_MissingPath_ReportsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WitchDrawer.Missing-{Guid.NewGuid():N}.txt");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => new ShellFileLauncher().OpenAsync(path));

        Assert.Equal(path, exception.FileName);
    }
}
