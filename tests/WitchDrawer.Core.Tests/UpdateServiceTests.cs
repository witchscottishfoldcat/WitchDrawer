using System.Diagnostics;
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

    [Fact]
    public void BuildUpdaterScript_CopiesOnlyPayloadAndCleansLegacyArtifacts()
    {
        var script = UpdateService.BuildUpdaterScript();

        Assert.Contains(
            "xcopy \"%WITCHDRAWER_PAYLOAD%\\*\" \"%WITCHDRAWER_APP_DIR%\"",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "xcopy \"%WITCHDRAWER_UPDATE_ROOT%\\*\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "del /q \"%WITCHDRAWER_APP_DIR%\\update.zip\" \"%WITCHDRAWER_APP_DIR%\\updater.bat\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "start \"\" /b /d \"%WITCHDRAWER_APP_DIR%\" \"%WITCHDRAWER_APP_EXE%\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "rmdir /s /q \"%WITCHDRAWER_UPDATE_ROOT%\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "del /q \"%~f0\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUpdaterStartInfo_UsesHiddenCmdWithoutShellExecution()
    {
        const string tempRoot = @"C:\Temp\WitchDrawer Update\run";
        const string updaterPath = tempRoot + @"\updater.bat";
        const string payloadPath = tempRoot + @"\payload";
        const string appDirectory = @"D:\应用\WitchDrawer";
        const string appExecutable = appDirectory + @"\WitchDrawer.App.exe";
        const string executableName = "WitchDrawer.App.exe";
        const string logPath = @"C:\Users\Test\AppData\Local\WitchDrawer\Logs\updater.log";

        var startInfo = UpdateService.CreateUpdaterStartInfo(
            updaterPath,
            tempRoot,
            payloadPath,
            appDirectory,
            appExecutable,
            executableName,
            logPath);

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(updaterPath), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Equal($"/d /s /c \"\"{updaterPath}\"\"", startInfo.Arguments);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal(tempRoot, startInfo.Environment["WITCHDRAWER_UPDATE_ROOT"]);
        Assert.Equal(payloadPath, startInfo.Environment["WITCHDRAWER_PAYLOAD"]);
        Assert.Equal(appDirectory, startInfo.Environment["WITCHDRAWER_APP_DIR"]);
        Assert.Equal(appExecutable, startInfo.Environment["WITCHDRAWER_APP_EXE"]);
        Assert.Equal(executableName, startInfo.Environment["WITCHDRAWER_EXE_NAME"]);
        Assert.Equal(logPath, startInfo.Environment["WITCHDRAWER_UPDATE_LOG"]);
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

    [Fact]
    public async Task UpdaterScript_CopiesPayloadRestartsTargetAndCleansTemporaryFiles()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawer Update Script Tests",
            Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "应用目录");
        var updaterPath = Path.Combine(testRoot, "WitchDrawer Updater.cmd");
        var executableName = "WitchDrawer.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var payloadExecutablePath = Path.Combine(payloadDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                payloadExecutablePath,
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo started\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "update.zip"), "legacy");
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "updater.bat"), "legacy");

            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath);
            startInfo.Environment["WITCHDRAWER_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            var updaterLog = File.Exists(logPath)
                ? await File.ReadAllTextAsync(logPath)
                : "Updater log was not created.";
            Assert.True(
                updaterProcess.ExitCode == 0,
                $"Updater exited with code {updaterProcess.ExitCode}.{Environment.NewLine}{updaterLog}");
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            await WaitForConditionAsync(() => !Directory.Exists(updateRoot), TimeSpan.FromSeconds(8));
            await WaitForConditionAsync(() => !File.Exists(updaterPath), TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(appExecutablePath));
            Assert.False(File.Exists(Path.Combine(appDirectory, "update.zip")));
            Assert.False(File.Exists(Path.Combine(appDirectory, "updater.bat")));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for updater condition.");
            await Task.Delay(50);
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
    }
}
