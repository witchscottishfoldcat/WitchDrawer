using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

[SupportedOSPlatform("windows")]
public sealed class UpdateFileOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rollback_RestoresOnlyReleaseFilesAndPreservesChangedUserData(bool hiddenReleaseFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.RollbackTests", Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "O'Brien 中文 & app");
        var payload = Path.Combine(root, "payload");
        var rollback = Path.Combine(root, "rollback");
        foreach (var directory in new[] { app, payload, rollback })
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
        }
        try
        {
            const string existingReleaseFile = "nested/core [library].dll";
            const string introducedReleaseFile = "nested/introduced.dll";
            const string userFile = "nested/user-data.txt";
            await File.WriteAllTextAsync(Path.Combine(payload, existingReleaseFile), "new-release");
            await File.WriteAllTextAsync(Path.Combine(app, existingReleaseFile), "new-release");
            await File.WriteAllTextAsync(Path.Combine(rollback, existingReleaseFile), "old-release");
            if (hiddenReleaseFile)
            {
                File.SetAttributes(Path.Combine(payload, existingReleaseFile), FileAttributes.Hidden);
                File.SetAttributes(Path.Combine(app, existingReleaseFile), FileAttributes.Hidden);
                File.SetAttributes(Path.Combine(rollback, existingReleaseFile), FileAttributes.Hidden);
            }
            await File.WriteAllTextAsync(Path.Combine(payload, introducedReleaseFile), "introduced");
            await File.WriteAllTextAsync(Path.Combine(app, introducedReleaseFile), "introduced");
            await File.WriteAllTextAsync(Path.Combine(rollback, userFile), "old-user-data");
            await File.WriteAllTextAsync(Path.Combine(app, userFile), "changed-after-backup");
            var newUserFile = Path.Combine(app, "new-user-data.txt");
            await File.WriteAllTextAsync(newUserFile, "created-after-backup");
            var removedUserFile = Path.Combine(rollback, "removed-user-data.txt");
            await File.WriteAllTextAsync(removedUserFile, "deleted-after-backup");
            var scriptPath = Path.Combine(root, "rollback.ps1");
            await File.WriteAllTextAsync(scriptPath, WindowsUpdateInstaller.BuildUpdaterFileOperationScript());
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["WITCHDRAWER_APP_DIR"] = app;
            startInfo.Environment["WITCHDRAWER_PAYLOAD"] = payload;
            startInfo.Environment["WITCHDRAWER_ROLLBACK"] = rollback;
            startInfo.Environment["WITCHDRAWER_FILE_OPERATION"] = "Rollback";
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var output = await stdout;
            var errors = await stderr;

            Assert.True(process.ExitCode < 8, $"Rollback failed: {process.ExitCode}: {output}: {errors}");
            Assert.Equal("old-release", await File.ReadAllTextAsync(Path.Combine(app, existingReleaseFile)));
            Assert.False(File.Exists(Path.Combine(app, introducedReleaseFile)));
            Assert.Equal("changed-after-backup", await File.ReadAllTextAsync(Path.Combine(app, userFile)));
            Assert.Equal("created-after-backup", await File.ReadAllTextAsync(newUserFile));
            Assert.False(File.Exists(Path.Combine(app, "removed-user-data.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileStep_WhenWriteProbeIsDenied_UsesElevationAndPreservesPathData(bool cancelElevation)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.ElevationTests", Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "O'Brien 中文 & app");
        var payload = Path.Combine(root, "payload");
        var rollback = Path.Combine(root, "rollback");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(rollback);
        var appDirectory = new DirectoryInfo(app);
        var originalAcl = appDirectory.GetAccessControl();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(app, "app.txt"), "old");
            await File.WriteAllTextAsync(Path.Combine(payload, "app.txt"), "new");
            var restrictedAcl = appDirectory.GetAccessControl();
            restrictedAcl.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
            appDirectory.SetAccessControl(restrictedAcl);
            var launchMarker = Path.Combine(root, "elevation-requested.txt");
            var scriptPath = Path.Combine(root, "file-step.ps1");
            // Deny new-file creation only in this fixture directory. Intercept the elevation
            // launch: execute its encoded worker as a normal
            // child process against the fixture, without prompting UAC or changing ACLs.
            var hooks = """
function Start-Process {
    param($FilePath, $Verb, $WindowStyle, $ArgumentList, [switch]$PassThru)
    if ($Verb -ne 'RunAs' -or $WindowStyle -ne 'Hidden') { throw 'Wrong elevation launch.' }
    [IO.File]::WriteAllText($env:WITCHDRAWER_ELEVATION_TEST_MARKER, $Verb)
    if ($env:WITCHDRAWER_ELEVATION_TEST_CANCEL -eq 'True') { throw [ComponentModel.Win32Exception]::new(1223) }
    & $FilePath @ArgumentList | Out-Null
    $result = [PSCustomObject]@{ ExitCode = $LASTEXITCODE }
    $result | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {} -PassThru
}
""";
            await File.WriteAllTextAsync(scriptPath, hooks + Environment.NewLine + WindowsUpdateInstaller.BuildUpdaterFileOperationScript());
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath }) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["WITCHDRAWER_APP_DIR"] = app;
            startInfo.Environment["WITCHDRAWER_PAYLOAD"] = payload;
            startInfo.Environment["WITCHDRAWER_ROLLBACK"] = rollback;
            startInfo.Environment["WITCHDRAWER_FILE_OPERATION"] = "Apply";
            startInfo.Environment["WITCHDRAWER_ELEVATION_TEST_MARKER"] = launchMarker;
            startInfo.Environment["WITCHDRAWER_ELEVATION_TEST_CANCEL"] = cancelElevation.ToString();
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var errors = await stderr;
            await stdout;

            Assert.True(File.Exists(launchMarker), $"Elevation was not requested: exit={process.ExitCode}: {errors}");
            Assert.Equal("RunAs", await File.ReadAllTextAsync(launchMarker));
            if (cancelElevation) Assert.Equal(1223, process.ExitCode);
            else Assert.True(process.ExitCode < 8, $"File step failed: {process.ExitCode}: {errors}");
            Assert.Equal(cancelElevation ? "old" : "new", await File.ReadAllTextAsync(Path.Combine(app, "app.txt")));
        }
        finally { appDirectory.SetAccessControl(originalAcl); Directory.Delete(root, recursive: true); }
    }
}
