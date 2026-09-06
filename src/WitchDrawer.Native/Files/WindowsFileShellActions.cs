using System.ComponentModel;
using System.Diagnostics;

namespace WitchDrawer.Native.Files;

/// <summary>
/// Read-only Windows Shell actions for drawer items. File persistence and file
/// mutation deliberately remain outside this integration class.
/// </summary>
public static class WindowsFileShellActions
{
    private const int OperationCanceledError = 1223;

    private static readonly HashSet<string> ElevatedLaunchExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe",
            ".com",
            ".bat",
            ".cmd",
            ".msi",
            ".msc",
            ".lnk"
        };

    public static bool CanRunAsAdministrator(string? path, bool isDirectory)
    {
        return !isDirectory
            && !string.IsNullOrWhiteSpace(path)
            && ElevatedLaunchExtensions.Contains(Path.GetExtension(path));
    }

    public static bool TryRunAsAdministrator(string path)
    {
        if (!CanRunAsAdministrator(path, isDirectory: false))
        {
            throw new InvalidOperationException("该项目不支持管理员启动。");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == OperationCanceledError)
        {
            return false;
        }
    }

    public static void RevealInFileExplorer(string path)
    {
        Process.Start(CreateRevealStartInfo(path));
    }

    internal static ProcessStartInfo CreateRevealStartInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath)
            ? new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            }
            : new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            };
    }
}
