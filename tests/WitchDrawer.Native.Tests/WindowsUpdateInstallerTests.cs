using System.Diagnostics;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class WindowsUpdateInstallerTests
{
    [Fact]
    public void BuildUpdaterScript_WaitsForGracefulExitAndUsesRecoverableOverlay()
    {
        var script = WindowsUpdateInstaller.BuildUpdaterScript();

        Assert.Contains("WITCHDRAWER_EXIT_WAIT_SECONDS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/MIR", script, StringComparison.Ordinal);
        Assert.Contains(
            "robocopy \"%WITCHDRAWER_APP_DIR%\" \"%WITCHDRAWER_ROLLBACK%\" /E",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "WITCHDRAWER_FILE_OPERATION=Apply",
            script,
            StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto backup_failed", script, StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto apply_failed", script, StringComparison.Ordinal);
        Assert.Contains("WITCHDRAWER_STARTUP_SUCCESS_MARKER", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("Test-Path", script, StringComparison.Ordinal);
        Assert.Contains(":rollback", script, StringComparison.Ordinal);
        Assert.Contains(
            "WITCHDRAWER_FILE_OPERATION=Rollback",
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

        var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
            updaterPath,
            tempRoot,
            payloadPath,
            appDirectory,
            appExecutable,
            executableName,
            logPath,
            appProcessId: 0,
            appProcessStartTimeUtcTicks: 0);

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
        Assert.Equal("0", startInfo.Environment["WITCHDRAWER_APP_PID"]);
        Assert.Equal("0", startInfo.Environment["WITCHDRAWER_APP_START_TIME_UTC_TICKS"]);
        Assert.Equal(
            Path.Combine(tempRoot, "startup-succeeded.marker"),
            startInfo.Environment["WITCHDRAWER_STARTUP_SUCCESS_MARKER"]);
    }

    [Fact]
    public async Task UpdaterScript_OverlaysPayloadPreservesUnrelatedFilesAndCleansTemporaryFiles()
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
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo started\r\n>\"%WITCHDRAWER_STARTUP_SUCCESS_MARKER%\" echo ready\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, WindowsUpdateInstaller.BuildUpdaterScript());
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "update.zip"), "legacy");
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "updater.bat"), "legacy");
            var stalePayloadPath = Path.Combine(appDirectory, "stale-old-version.dll");
            await File.WriteAllTextAsync(stalePayloadPath, "stale");
            var replacedPath = Path.Combine(appDirectory, "WitchDrawer.Core.dll");
            await File.WriteAllTextAsync(replacedPath, "old");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, "WitchDrawer.Core.dll"),
                "new");

            var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
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
            Assert.Equal("new", await File.ReadAllTextAsync(replacedPath));
            Assert.True(File.Exists(stalePayloadPath));
            Assert.Equal("stale", await File.ReadAllTextAsync(stalePayloadPath));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_WaitsForOriginalProcessExitBeforeApplyingPayload()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer Update Wait Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "WitchDrawer.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo started\r\n>\"%WITCHDRAWER_STARTUP_SUCCESS_MARKER%\" echo ready\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, WindowsUpdateInstaller.BuildUpdaterScript());
            await File.WriteAllTextAsync(appExecutablePath, "old");

            using var originalProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 3\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(originalProcess);

            var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                originalProcess.Id,
                originalProcess.StartTime.ToUniversalTime().Ticks);
            startInfo.Environment["WITCHDRAWER_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await Task.Delay(500);
            Assert.False(File.Exists(markerPath));
            Assert.Equal("old", await File.ReadAllTextAsync(appExecutablePath));

            await originalProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, updaterProcess.ExitCode);
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestoresPreviousInstallationAfterPayloadCopyFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer Update Rollback Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "WitchDrawer.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");
        var firstAppPath = Path.Combine(appDirectory, "a-first.dll");
        var introducedAppPath = Path.Combine(appDirectory, "b-introduced.dll");
        var lockedPayloadPath = Path.Combine(payloadDirectory, "z-locked.dll");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(firstAppPath, "old-first");
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "z-locked.dll"), "old-locked");
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo restored\r\nexit\r\n");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "a-first.dll"), "new-first");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "b-introduced.dll"), "new-introduced");
            await File.WriteAllTextAsync(lockedPayloadPath, "new-locked");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo updated\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, WindowsUpdateInstaller.BuildUpdaterScript());

            await using var lockStream = new FileStream(
                lockedPayloadPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["WITCHDRAWER_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await WaitForConditionAsync(() => File.Exists(introducedAppPath), TimeSpan.FromSeconds(5));
            // This proves a partial copy occurred. The updater may still hold
            // the file open or already be deleting it as part of rollback.
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Equal("old-first", await File.ReadAllTextAsync(firstAppPath));
            Assert.False(File.Exists(introducedAppPath));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("restored", (await File.ReadAllTextAsync(markerPath)).Trim());
            Assert.True(Directory.Exists(Path.Combine(updateRoot, "rollback")));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestartsOriginalApplicationWhenBackupFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer Update Backup Failure Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "WitchDrawer.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");
        var lockedAppPath = Path.Combine(appDirectory, "z-locked.dll");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo original\r\nexit\r\n");
            await File.WriteAllTextAsync(lockedAppPath, "old-locked");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo updated\r\nexit\r\n");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "z-locked.dll"), "new-locked");
            await File.WriteAllTextAsync(updaterPath, WindowsUpdateInstaller.BuildUpdaterScript());

            await using var lockStream = new FileStream(
                lockedAppPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["WITCHDRAWER_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Contains("echo original", await File.ReadAllTextAsync(appExecutablePath));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("original", (await File.ReadAllTextAsync(markerPath)).Trim());
            Assert.True(Directory.Exists(updateRoot));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestoresOriginalApplicationWhenUpdatedStartupIsNotConfirmed()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer Update Startup Failure Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "WitchDrawer.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");
        var userDataPath = Path.Combine(appDirectory, "user-data.txt");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%WITCHDRAWER_TEST_MARKER%\" echo restored\r\nexit\r\n");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%WITCHDRAWER_TEST_USER_DATA%\" echo changed-after-backup\r\nexit /b 1\r\n");
            await File.WriteAllTextAsync(userDataPath, "original-user-data");
            await File.WriteAllTextAsync(updaterPath, WindowsUpdateInstaller.BuildUpdaterScript());

            var startInfo = WindowsUpdateInstaller.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["WITCHDRAWER_TEST_MARKER"] = markerPath;
            startInfo.Environment["WITCHDRAWER_TEST_USER_DATA"] = userDataPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Contains("echo restored", await File.ReadAllTextAsync(appExecutablePath));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("restored", (await File.ReadAllTextAsync(markerPath)).Trim());
            Assert.Equal("changed-after-backup", (await File.ReadAllTextAsync(userDataPath)).Trim());
            Assert.True(Directory.Exists(Path.Combine(updateRoot, "rollback")));
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
