using System.Diagnostics;
using System.Text;
using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Windows;

public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    private const string StartupSuccessMarkerEnvironmentVariable = "WITCHDRAWER_STARTUP_SUCCESS_MARKER";
    private const string StartupSuccessMarkerFileName = "startup-succeeded.marker";

    public async Task<bool> StartAsync(UpdateInstallRequest request)
    {
        var updaterPath = Path.Combine(Path.GetTempPath(), $"WitchDrawerUpdater-{request.UpdateId}.bat");
        try
        {
            await File.WriteAllTextAsync(updaterPath, BuildUpdaterScript(), Encoding.ASCII);
            using var helper = Process.Start(CreateUpdaterStartInfo(
                updaterPath, request.TempRoot, request.PayloadDirectory, request.AppDirectory,
                request.AppExecutablePath, request.ExecutableName, request.LogPath,
                request.ProcessId, request.ProcessStartTimeUtcTicks));
            if (helper is not null) return true;
        }
        catch
        {
            try { File.Delete(updaterPath); } catch { }
            throw;
        }
        try { File.Delete(updaterPath); } catch { }
        return false;
    }

    internal static string BuildUpdaterScript()
    {
        var helperData = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildUpdaterFileOperationScript()));
        var helperLines = helperData.Chunk(1000).Select((chunk, index) =>
            $"{(index == 0 ? ">" : ">>")}\"%WITCHDRAWER_FILE_HELPER_DATA%\" echo {new string(chunk)}");
        return """"
@echo off
setlocal
set "WITCHDRAWER_ROLLBACK=%WITCHDRAWER_UPDATE_ROOT%\rollback"
set "WITCHDRAWER_FILE_HELPER=%WITCHDRAWER_UPDATE_ROOT%\file-operations.ps1"
set "WITCHDRAWER_FILE_HELPER_DATA=%WITCHDRAWER_UPDATE_ROOT%\file-operations.base64"
@@WRITE_FILE_HELPER@@
powershell.exe -NoProfile -Command "$data = [IO.File]::ReadAllText($env:WITCHDRAWER_FILE_HELPER_DATA); [IO.File]::WriteAllText($env:WITCHDRAWER_FILE_HELPER, [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($data)), [Text.Encoding]::UTF8)"
if errorlevel 1 exit /b 1
set "WITCHDRAWER_EXIT_WAIT_SECONDS=30"
set "WITCHDRAWER_STARTUP_WAIT_SECONDS=60"
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update started.

rem Wait for this exact process instance to exit naturally before replacing files.
if "%WITCHDRAWER_APP_PID%"=="0" goto process_exited
for /l %%i in (1,1,%WITCHDRAWER_EXIT_WAIT_SECONDS%) do (
    powershell.exe -NoProfile -Command "$process = Get-Process -Id %WITCHDRAWER_APP_PID% -ErrorAction SilentlyContinue; if ($null -eq $process -or $process.StartTime.ToUniversalTime().Ticks -ne %WITCHDRAWER_APP_START_TIME_UTC_TICKS%) { exit 0 }; exit 1" >nul 2>&1
    if not errorlevel 1 goto process_exited
    timeout /t 1 /nobreak >nul
)
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Timed out waiting for the application process to exit.
exit /b 1

:process_exited
rem robocopy exit codes 0-7 are successful; 8 and above are failures.
robocopy "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_ROLLBACK%" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NP >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 8 goto backup_failed

rem Overlay only the release payload. Do not delete files that are not part of the release.
set "WITCHDRAWER_FILE_OPERATION=Apply"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%WITCHDRAWER_FILE_HELPER%" >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 1223 goto elevation_cancelled
if errorlevel 8 goto apply_failed

del /q "%WITCHDRAWER_APP_DIR%\update.zip" "%WITCHDRAWER_APP_DIR%\updater.bat" >nul 2>&1
del /q "%WITCHDRAWER_STARTUP_SUCCESS_MARKER%" >nul 2>&1

powershell.exe -NoProfile -Command "$process = Start-Process -FilePath $env:WITCHDRAWER_APP_EXE -WorkingDirectory $env:WITCHDRAWER_APP_DIR -PassThru; $deadline = [DateTime]::UtcNow.AddSeconds([int]$env:WITCHDRAWER_STARTUP_WAIT_SECONDS); while ([DateTime]::UtcNow -lt $deadline) { if (Test-Path -LiteralPath $env:WITCHDRAWER_STARTUP_SUCCESS_MARKER -PathType Leaf) { exit 0 }; if ($process.HasExited) { exit 1 }; Start-Sleep -Milliseconds 200 }; if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() }; exit 1" >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 1 goto start_failed

>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Updated application confirmed startup. Update completed.
cd /d "%TEMP%"
rmdir /s /q "%WITCHDRAWER_UPDATE_ROOT%" >nul 2>&1
start "" /b "%ComSpec%" /d /c del /q "%~f0" >nul 2>&1 & exit /b 0

:backup_failed
set "WITCHDRAWER_FAILURE_CODE=%errorlevel%"
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update backup failed with exit code %WITCHDRAWER_FAILURE_CODE%. Restarting the original installation.
set "WITCHDRAWER_STARTUP_SUCCESS_MARKER="
start "" /b /d "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_APP_EXE%" >nul 2>&1
if errorlevel 1 goto original_restart_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Original installation restarted after backup failure.
exit /b 1

:original_restart_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Failed to restart the original installation after backup failure.
exit /b 1

:elevation_cancelled
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update elevation cancelled. Restarting the unchanged installation.
set "WITCHDRAWER_STARTUP_SUCCESS_MARKER="
start "" /b /d "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_APP_EXE%" >nul 2>&1
exit /b 1

:apply_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update apply failed with exit code %errorlevel%. Restoring the previous installation.
goto rollback

:start_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Updated application failed to start. Restoring the previous installation.

:rollback
rem Remove only files introduced by this payload that were absent from the backup.
set "WITCHDRAWER_FILE_OPERATION=Rollback"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%WITCHDRAWER_FILE_HELPER%" >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 8 goto rollback_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Previous installation restored. Recovery files retained at "%WITCHDRAWER_UPDATE_ROOT%".
set "WITCHDRAWER_STARTUP_SUCCESS_MARKER="
start "" /b /d "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_APP_EXE%" >nul 2>&1
if errorlevel 1 goto rollback_failed
exit /b 1

:rollback_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update rollback failed. Recovery files retained at "%WITCHDRAWER_UPDATE_ROOT%".
exit /b 1
"""".Replace("@@WRITE_FILE_HELPER@@", string.Join(Environment.NewLine, helperLines), StringComparison.Ordinal);
    }

    // The original, unelevated updater owns process waiting, restart and startup confirmation.
    // Only this isolated file step elevates, so drag/drop and the startup marker still work.
    internal static string BuildUpdaterFileOperationScript() => """
$ErrorActionPreference = 'Stop'
function Invoke-UpdateFiles($plan) {
    $ErrorActionPreference = 'Stop'
    try {
        $app = [IO.Path]::GetFullPath($plan.AppDirectory).TrimEnd('\')
        $payload = [IO.Path]::GetFullPath($plan.PayloadDirectory).TrimEnd('\')
        $rollback = [IO.Path]::GetFullPath($plan.RollbackDirectory).TrimEnd('\')
        if ($plan.Operation -eq 'Rollback') {
            Get-ChildItem -LiteralPath $payload -Recurse -File -Force | ForEach-Object {
                $relative = $_.FullName.Substring($payload.Length).TrimStart('\')
                $target = [IO.Path]::GetFullPath((Join-Path $app $relative))
                if (-not $target.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid rollback target.' }
                $backup = [IO.Path]::GetFullPath((Join-Path $rollback $relative))
                if (-not $backup.StartsWith($rollback + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid rollback source.' }
                if (Test-Path -LiteralPath $backup -PathType Leaf) {
                    # Restore only release files. The full backup can include user data
                    # that changed after the updated application started.
                    & "$env:SystemRoot\System32\robocopy.exe" ([IO.Path]::GetDirectoryName($backup)) ([IO.Path]::GetDirectoryName($target)) ([IO.Path]::GetFileName($backup)) /IS /IT /COPY:DAT /R:1 /W:1 /NFL /NDL /NP
                    if ($LASTEXITCODE -ge 8) { throw "Rollback copy failed with exit code $LASTEXITCODE." }
                } elseif (Test-Path -LiteralPath $target -PathType Leaf) {
                    Remove-Item -LiteralPath $target -Force -ErrorAction Stop
                }
            }
            exit 0
        } elseif ($plan.Operation -eq 'Apply') {
            $source = $payload
        } else { throw 'Invalid update operation.' }
        & "$env:SystemRoot\System32\robocopy.exe" $source $app /E /IS /IT /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NP
        exit $LASTEXITCODE
    } catch {
        Write-Error $_ -ErrorAction Continue
        exit 8
    }
}
$plan = @{
    AppDirectory = $env:WITCHDRAWER_APP_DIR
    PayloadDirectory = $env:WITCHDRAWER_PAYLOAD
    RollbackDirectory = $env:WITCHDRAWER_ROLLBACK
    Operation = $env:WITCHDRAWER_FILE_OPERATION
}
$probePath = Join-Path $plan.AppDirectory ('.witchdrawer-write-probe-' + [Guid]::NewGuid().ToString('N'))
$needsElevation = $false
try {
    $probe = [IO.File]::Open($probePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $probe.Dispose()
    [IO.File]::Delete($probePath)
} catch {
    $exception = $_.Exception
    while ($exception.InnerException) { $exception = $exception.InnerException }
    if ($exception -is [UnauthorizedAccessException]) { $needsElevation = $true }
    else { Write-Error $_ -ErrorAction Continue; exit 8 }
}
if (-not $needsElevation) { Invoke-UpdateFiles $plan }

# Shell elevation does not inherit our environment. Serialize paths as data and
# embed only base64 in the command line so quotes/non-ASCII paths stay intact.
$data = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($plan | ConvertTo-Json -Compress)))
$command = '$plan = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(''' + $data + ''')) | ConvertFrom-Json; & {' + ${function:Invoke-UpdateFiles}.ToString() + '} $plan'
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
try {
    $worker = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -PassThru
    $worker.WaitForExit()
    exit $worker.ExitCode
} catch {
    $exception = $_.Exception
    while ($exception.InnerException) { $exception = $exception.InnerException }
    if ($exception -is [ComponentModel.Win32Exception] -and $exception.NativeErrorCode -eq 1223) { exit 1223 }
    Write-Error $_ -ErrorAction Continue
    exit 8
}
""";

    internal static ProcessStartInfo CreateUpdaterStartInfo(
        string updaterPath,
        string tempRoot,
        string payloadDirectory,
        string appDirectory,
        string appExecutablePath,
        string executableName,
        string updateLogPath,
        int appProcessId,
        long appProcessStartTimeUtcTicks)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /s /c \"\"{updaterPath}\"\"",
            WorkingDirectory = Path.GetDirectoryName(updaterPath)
                ?? Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.Environment["WITCHDRAWER_UPDATE_ROOT"] = tempRoot;
        startInfo.Environment["WITCHDRAWER_PAYLOAD"] = payloadDirectory;
        startInfo.Environment["WITCHDRAWER_APP_DIR"] = appDirectory;
        startInfo.Environment["WITCHDRAWER_APP_EXE"] = appExecutablePath;
        startInfo.Environment["WITCHDRAWER_EXE_NAME"] = executableName;
        startInfo.Environment["WITCHDRAWER_UPDATE_LOG"] = updateLogPath;
        startInfo.Environment["WITCHDRAWER_APP_PID"] = appProcessId.ToString();
        startInfo.Environment["WITCHDRAWER_APP_START_TIME_UTC_TICKS"] =
            appProcessStartTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment[StartupSuccessMarkerEnvironmentVariable] =
            Path.Combine(tempRoot, StartupSuccessMarkerFileName);
        return startInfo;
    }

}
