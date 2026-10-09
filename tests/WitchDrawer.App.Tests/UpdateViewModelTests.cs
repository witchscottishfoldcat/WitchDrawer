using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Tests;

public sealed class UpdateViewModelTests
{
    [Fact]
    public async Task CancelDownload_ReleasesBusyStateAndAllowsRetry()
    {
        using var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        using var installer = new TestInstaller();
        var viewModel = CreateViewModel(client, installer, _ => Task.FromResult(true));
        var operation = viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        await handler.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(viewModel.CancelUpdateCommand.CanExecute(null));
        viewModel.CancelUpdateCommand.Execute(null);
        await operation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("更新已取消", viewModel.UpdateStatusText);
        Assert.False(viewModel.IsCheckingUpdate);
        Assert.False(viewModel.CancelUpdateCommand.CanExecute(null));
        Assert.True(viewModel.CheckForUpdateCommand.CanExecute(null));
        Assert.Equal(0, installer.Calls);

        handler.DownloadStatus = HttpStatusCode.ServiceUnavailable;
        handler.DownloadRelease.SetResult();
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.Equal("下载更新失败", viewModel.UpdateStatusText);
        Assert.Equal(2, handler.Downloads);
    }

    [Fact]
    public async Task UpdateCommand_RemainsExclusiveThroughConfirmationDownloadInstallerAndShutdown()
    {
        using var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        using var installer = new TestInstaller();
        var confirmation = Signal<bool>();
        var confirmationEntered = Signal();
        var shutdown = Signal();
        var shutdownEntered = Signal();
        var viewModel = CreateViewModel(client, installer,
            _ => { confirmationEntered.TrySetResult(); return confirmation.Task; },
            () => { shutdownEntered.TrySetResult(); return shutdown.Task; });

        var operation = viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        try
        {
            await confirmationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertBusyAndRejectDuplicateAsync(viewModel, operation);
            Assert.Equal(1, handler.Checks);

            confirmation.SetResult(true);
            await handler.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertBusyAndRejectDuplicateAsync(viewModel, operation);
            Assert.Equal(1, handler.Downloads);

            handler.DownloadRelease.SetResult();
            await installer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertBusyAndRejectDuplicateAsync(viewModel, operation);
            Assert.Equal(1, installer.Calls);

            installer.Result.SetResult(true);
            await shutdownEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertBusyAndRejectDuplicateAsync(viewModel, operation);
            Assert.Equal(1, handler.Checks);
            Assert.Equal(1, handler.Downloads);
            Assert.Equal(1, installer.Calls);
        }
        finally
        {
            confirmation.TrySetResult(false);
            handler.DownloadRelease.TrySetResult();
            installer.Result.TrySetResult(false);
            shutdown.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Even after the shutdown callback returns, a started updater must not be repeated.
        Assert.True(viewModel.IsCheckingUpdate);
        Assert.False(viewModel.CheckForUpdateCommand.CanExecute(null));
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, handler.Checks);
    }

    [Fact]
    public async Task CancelingConfirmation_AllowsAnotherCheckWithoutDownloading()
    {
        using var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        using var installer = new TestInstaller();
        var viewModel = CreateViewModel(client, installer, _ => Task.FromResult(false));

        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsCheckingUpdate);
        Assert.True(viewModel.CheckForUpdateCommand.CanExecute(null));
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, handler.Checks);
        Assert.Equal(0, handler.Downloads);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task DownloadFailure_ReleasesBusyStateAndAllowsRetry()
    {
        using var handler = new ReleaseHandler { DownloadStatus = HttpStatusCode.ServiceUnavailable };
        handler.DownloadRelease.SetResult();
        using var client = new HttpClient(handler);
        using var installer = new TestInstaller();
        var viewModel = CreateViewModel(client, installer, _ => Task.FromResult(true));

        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.Equal("下载更新失败", viewModel.UpdateStatusText);
        Assert.False(viewModel.IsCheckingUpdate);
        Assert.True(viewModel.CheckForUpdateCommand.CanExecute(null));
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, handler.Downloads);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task CheckFailure_ShowsFailureInsteadOfLatestVersionAndAllowsRetry()
    {
        using var handler = new ReleaseHandler { CheckStatus = HttpStatusCode.ServiceUnavailable };
        using var client = new HttpClient(handler);
        using var installer = new TestInstaller();
        var state = new UiOperationState(NullAppLogger.Instance);
        var viewModel = new UpdateViewModel(
            new UpdateService(NullAppLogger.Instance, installer, client), NullAppLogger.Instance, state);

        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.Equal("检查更新失败", viewModel.UpdateStatusText);
        Assert.Equal("检查更新失败", state.StatusText);
        Assert.False(viewModel.IsCheckingUpdate);
        Assert.True(viewModel.CheckForUpdateCommand.CanExecute(null));
        Assert.Equal(0, handler.Downloads);

        handler.CheckStatus = HttpStatusCode.OK;
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.Equal("发现新版本 v99.0.0", viewModel.UpdateStatusText);
        Assert.Equal(2, handler.Checks);
    }

    private static UpdateViewModel CreateViewModel(HttpClient client, IUpdateInstaller installer,
        Func<UpdateCheckResult, Task<bool>> confirm, Func<Task>? shutdown = null) =>
        new(new UpdateService(NullAppLogger.Instance, installer, client), NullAppLogger.Instance,
            new UiOperationState(NullAppLogger.Instance), confirm, shutdown);

    private static async Task AssertBusyAndRejectDuplicateAsync(UpdateViewModel viewModel, Task operation)
    {
        Assert.False(operation.IsCompleted);
        Assert.True(viewModel.IsCheckingUpdate);
        Assert.False(viewModel.CheckForUpdateCommand.CanExecute(null));
        // Exercise the method guard as well as the button's CanExecute guard.
        await viewModel.CheckForUpdateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(viewModel.IsCheckingUpdate);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        private const string PackageName = "WitchDrawer-v99.0.0-win-x64.zip";
        private const string PackageUrl =
            "https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v99.0.0/" + PackageName;
        private readonly byte[] _package = CreatePackage();
        public TaskCompletionSource DownloadEntered { get; } = Signal();
        public TaskCompletionSource DownloadRelease { get; } = Signal();
        public HttpStatusCode CheckStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode DownloadStatus { get; set; } = HttpStatusCode.OK;
        public int Checks;
        public int Downloads;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                Interlocked.Increment(ref Checks);
                return new HttpResponseMessage(CheckStatus)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        tag_name = "v99.0.0",
                        assets = new[]
                        {
                            new { name = PackageName, browser_download_url = PackageUrl },
                            new { name = PackageName + ".sha256", browser_download_url = PackageUrl + ".sha256" }
                        }
                    }))
                };
            }

            if (request.RequestUri.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Convert.ToHexString(SHA256.HashData(_package)) + "  " + PackageName)
                };

            Interlocked.Increment(ref Downloads);
            DownloadEntered.TrySetResult();
            await DownloadRelease.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(DownloadStatus) { Content = new ByteArrayContent(_package) };
        }

        private static byte[] CreatePackage()
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry(Path.GetFileName(Environment.ProcessPath!)).Open()))
                writer.Write("test payload; never executed");
            return stream.ToArray();
        }
    }

    private sealed class TestInstaller : IUpdateInstaller, IDisposable
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource<bool> Result { get; } = Signal<bool>();
        private UpdateInstallRequest? _request;
        public int Calls;

        public Task<bool> StartAsync(UpdateInstallRequest request)
        {
            _request = request;
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            return Result.Task;
        }

        public void Dispose()
        {
            if (_request is not { } request) return;
            var root = Path.GetFullPath(request.TempRoot);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerUpdate", request.UpdateId));
            Assert.Equal(expectedRoot, root);
            Assert.True(Guid.TryParseExact(request.UpdateId, "N", out _));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
