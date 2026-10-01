using System.Diagnostics;

namespace WitchDrawer.App.Infrastructure;

internal static class ApplicationRestart
{
    internal static ProcessStartInfo CreateStartInfo(
        string executablePath, string workingDirectory, int processId, long processStartTimeUtcTicks)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $"while ($process = Get-Process -Id {processId} -ErrorAction SilentlyContinue) "
            + $"{{ if ($process.StartTime.ToUniversalTime().Ticks -ne {processStartTimeUtcTicks}) {{ break }}; "
            + "Start-Sleep -Milliseconds 300 }; "
            + "Start-Process -FilePath $env:WITCHDRAWER_RESTART_EXE -WorkingDirectory $env:WITCHDRAWER_RESTART_DIRECTORY");
        // Paths are data, even when they contain quotes, dollar signs or PowerShell syntax.
        startInfo.Environment["WITCHDRAWER_RESTART_EXE"] = executablePath;
        startInfo.Environment["WITCHDRAWER_RESTART_DIRECTORY"] = workingDirectory;
        return startInfo;
    }
}
