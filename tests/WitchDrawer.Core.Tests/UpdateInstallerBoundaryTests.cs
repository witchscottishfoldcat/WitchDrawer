using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class UpdateInstallerBoundaryTests
{
    private const string DownloadUrl = "https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.3.15/update.zip";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task PreparedPackage_IsPassedToInstaller_AndFailedLaunchCleansPayload(bool started, bool throws)
    {
        var executableName = Path.GetFileName(Environment.ProcessPath!);
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(executableName).Open());
            writer.Write("validated payload");
        }
        var package = zip.ToArray();
        using var client = new HttpClient(new PackageHandler(package));
        var installer = new Installer(request =>
        {
            Assert.Equal(Environment.ProcessId, request.ProcessId);
            Assert.True(request.ProcessStartTimeUtcTicks > 0);
            Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), request.AppExecutablePath);
            Assert.Equal(executableName, request.ExecutableName);
            Assert.Equal("validated payload", File.ReadAllText(Path.Combine(request.PayloadDirectory, executableName)));
            if (throws) throw new IOException("helper launch failed");
            return started;
        });
        var service = new UpdateService(NullAppLogger.Instance, installer, client);
        try
        {
            var result = await service.DownloadAndApplyUpdateAsync(DownloadUrl,
                expectedSha256: Convert.ToHexString(SHA256.HashData(package)));
            Assert.Equal(started, result);
            Assert.NotNull(installer.Request);
            Assert.Equal(started, Directory.Exists(installer.Request.TempRoot));
        }
        finally
        {
            if (installer.Request is { } request && Directory.Exists(request.TempRoot))
                Directory.Delete(request.TempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPlatformInstaller_RejectsApplyBeforeDownloading()
    {
        var handler = new PackageHandler([]);
        using var client = new HttpClient(handler);
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);
        Assert.False(await service.DownloadAndApplyUpdateAsync(DownloadUrl));
        Assert.Equal(0, handler.Requests);
    }

    private sealed class Installer(Func<UpdateInstallRequest, bool> start) : IUpdateInstaller
    {
        internal UpdateInstallRequest? Request { get; private set; }
        public Task<bool> StartAsync(UpdateInstallRequest request)
        {
            Request = request;
            return Task.FromResult(start(request));
        }
    }

    private sealed class PackageHandler(byte[] package) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
        }
    }
}
