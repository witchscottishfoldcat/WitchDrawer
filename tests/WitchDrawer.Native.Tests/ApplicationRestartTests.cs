using System.Diagnostics;
using System.IO;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class ApplicationRestartTests
{
    [Theory]
    [InlineData("O'Brien")]
    [InlineData("中文 空格")]
    [InlineData("$name & (test)")]
    public async Task Restart_WaitsForOriginalProcessAndTreatsPathsAsData(string directoryName)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.RestartTests", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, directoryName);
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "restart target.cmd");
            var marker = Path.Combine(root, "started.txt");
            await File.WriteAllTextAsync(executable, "@echo off\r\n>\"%WITCHDRAWER_RESTART_TEST_MARKER%\" echo started\r\nexit\r\n");
            using var original = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 2\"",
                UseShellExecute = false, CreateNoWindow = true
            })!;
            var startInfo = ApplicationRestart.CreateStartInfo(executable, directory, original.Id, original.StartTime.ToUniversalTime().Ticks);
            startInfo.Environment["WITCHDRAWER_RESTART_TEST_MARKER"] = marker;
            // Observe the target's lifetime as well as the helper's. The marker is
            // written before cmd.exe releases the script and working directory.
            startInfo.ArgumentList[^1] += " -WindowStyle Hidden -PassThru | ForEach-Object { $_.WaitForExit() }";
            using var helper = Process.Start(startInfo)!;
            try
            {
                await Task.Delay(300);
                Assert.False(File.Exists(marker));
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, helper.ExitCode);
                Assert.True(File.Exists(marker));
            }
            finally
            {
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await original.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
