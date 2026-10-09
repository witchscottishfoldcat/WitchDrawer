using System.Net;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class UpdateDownloadCancellationTests
{
    private const string DownloadUrl = "https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v99.0.0/update.zip";

    [Fact]
    public async Task HeadersReceived_BodyStallTimesOutAndCleansPartialPackage()
    {
        using var body = new DownloadStream();
        using var client = new HttpClient(new Handler(body)) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new Installer();
        var logger = new Logger();
        var service = new UpdateService(logger, installer, client, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10));
        Assert.False(await service.DownloadAndApplyUpdateAsync(DownloadUrl).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(body.ReadStarted.Task.IsCompleted);
        Assert.Equal(0, installer.Calls);
        Assert.Single(logger.Errors);
        AssertCleaned(logger);
    }

    [Fact]
    public async Task BodyRead_UserCancellationCleansPartialPackageWithoutStartingInstaller()
    {
        using var body = new DownloadStream();
        using var client = new HttpClient(new Handler(body)) { Timeout = Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource();
        var installer = new Installer();
        var logger = new Logger();
        var service = new UpdateService(logger, installer, client);
        var download = service.DownloadAndApplyUpdateAsync(DownloadUrl, cancellationToken: cancellation.Token);
        await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, installer.Calls);
        Assert.Empty(logger.Errors);
        AssertCleaned(logger);
    }

    [Fact]
    public async Task ContinuousTrickle_CannotExtendTotalDownloadDeadline()
    {
        using var body = new DownloadStream(trickle: true);
        using var client = new HttpClient(new Handler(body)) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new Installer();
        var logger = new Logger();
        var service = new UpdateService(logger, installer, client, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300));
        Assert.False(await service.DownloadAndApplyUpdateAsync(DownloadUrl).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(body.Reads > 1);
        Assert.Equal(0, installer.Calls);
        AssertCleaned(logger);
    }

    [Fact]
    public async Task AlreadyCanceled_DoesNotMakeHttpRequest()
    {
        using var body = new DownloadStream();
        var handler = new Handler(body);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new UpdateService(NullAppLogger.Instance, new Installer(), client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DownloadAndApplyUpdateAsync(DownloadUrl, cancellationToken: cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private static void AssertCleaned(Logger logger)
    {
        const string prefix = "Downloading update into ";
        var line = Assert.Single(logger.Information, message => message.StartsWith(prefix, StringComparison.Ordinal));
        var root = line[prefix.Length..^1];
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerUpdate")), Path.GetDirectoryName(root));
        Assert.True(Guid.TryParseExact(Path.GetFileName(root), "N", out _));
        Assert.False(Directory.Exists(root));
    }

    private sealed class Logger : IAppLogger
    {
        internal List<string> Information { get; } = [];
        internal List<Exception> Errors { get; } = [];
        public void Info(string message) => Information.Add(message);
        public void Error(Exception exception, string message) => Errors.Add(exception);
    }

    private sealed class Installer : IUpdateInstaller
    {
        internal int Calls;
        public Task<bool> StartAsync(UpdateInstallRequest request) { Calls++; return Task.FromResult(false); }
    }

    private sealed class Handler(Stream body) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        }
    }

    private sealed class DownloadStream(bool trickle = false) : Stream
    {
        internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads > 1)
            {
                ReadStarted.TrySetResult();
                await Task.Delay(trickle ? 25 : Timeout.Infinite, cancellationToken);
            }
            buffer.Span[0] = 42;
            return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
