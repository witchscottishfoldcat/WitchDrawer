using System.Diagnostics;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0.2/app.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123/abc", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/123/abc", true)]
    [InlineData("http://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0.2/app.zip", false)]
    [InlineData("https://evil.example/update.zip", false)]
    [InlineData("https://github.com/other/other/releases/download/v1.0.2/app.zip", false)]
    [InlineData("not-a-url", false)]
    public void IsAllowedDownloadUrl_FiltersUnexpectedHosts(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsAllowedDownloadUrl(url));
    }

    [Theory]
    [InlineData("1.3.1", "1.3", true)]
    [InlineData("1.3", "1.3.0", false)]
    [InlineData("1.3.0", "1.3", false)]
    [InlineData("1.3.5.0", "1.3.5", false)]
    [InlineData("1.3.5.1", "1.3.5", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.2.9", "1.3", false)]
    public void IsNewerVersion_TreatsMissingComponentsAsZero(
        string remote,
        string current,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateService.IsNewerVersion(Version.Parse(remote), Version.Parse(current)));
    }

    [Fact]
    public async Task ConfirmUpdateStartupAsync_WritesMarkerInsideGeneratedUpdateRoot()
    {
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerUpdate",
            Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(updateRoot, "startup-succeeded.marker");
        Directory.CreateDirectory(updateRoot);

        try
        {
            var service = new UpdateService(new ThrowingLogger());

            var confirmed = await service.ConfirmUpdateStartupAsync(markerPath);

            Assert.True(confirmed);
            Assert.True(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(updateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ConfirmUpdateStartupAsync_RejectsMarkerOutsideGeneratedUpdateRoot()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawer Invalid Update Marker Tests",
            Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(testRoot, "startup-succeeded.marker");
        Directory.CreateDirectory(testRoot);

        try
        {
            var service = new UpdateService(NullAppLogger.Instance);

            var confirmed = await service.ConfirmUpdateStartupAsync(markerPath);

            Assert.False(confirmed);
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void CleanupLegacyUpdaterArtifacts_DeletesOnlyKnownResidue()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawer Legacy Cleanup Tests",
            Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(testRoot, "应用目录");
        Directory.CreateDirectory(appDirectory);

        try
        {
            var zipPath = Path.Combine(appDirectory, "update.zip");
            var updaterPath = Path.Combine(appDirectory, "updater.bat");
            var appPath = Path.Combine(appDirectory, "WitchDrawer.App.exe");
            File.WriteAllText(zipPath, "legacy");
            File.WriteAllText(updaterPath, "legacy");
            File.WriteAllText(appPath, "keep");

            var removedCount = UpdateService.CleanupLegacyUpdaterArtifacts(appDirectory);

            Assert.Equal(2, removedCount);
            Assert.False(File.Exists(zipPath));
            Assert.False(File.Exists(updaterPath));
            Assert.True(File.Exists(appPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private sealed class ThrowingLogger : IAppLogger
    {
        public void Info(string message)
        {
            throw new IOException("log write failed");
        }

        public void Error(Exception exception, string message)
        {
            throw new IOException("log write failed");
        }
    }
}
