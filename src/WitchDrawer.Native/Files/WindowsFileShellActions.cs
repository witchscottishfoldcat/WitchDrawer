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
            using var process = Process.Start(new ProcessStartInfo
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
        using var process = Process.Start(CreateRevealStartInfo(path));
    }

    public static Task<bool> RunAsAdministratorAsync(string path, CancellationToken cancellationToken = default)
        => StaShellWorker.RunAsync(() => TryRunAsAdministrator(path), cancellationToken);

    public static Task RevealAsync(string path, CancellationToken cancellationToken = default)
        => StaShellWorker.RunAsync(() =>
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("文件或文件夹已不存在。", path);
            RevealInFileExplorer(path);
            return true;
        }, cancellationToken);

    public static bool IsShortcut(string path) => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    public static Task<string> GetShortcutTargetAsync(string path, CancellationToken cancellationToken = default)
        => StaShellWorker.RunAsync(() => ResolveShortcutTarget(path), cancellationToken);

    internal static string ResolveShortcutTarget(string path)
    {
        var raw = ShellIconExtractor.TryGetShortcutTargetPath(path);
        if (string.IsNullOrWhiteSpace(raw)) throw new IOException("快捷方式没有可访问的本地目标。");
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw new IOException("此快捷方式指向网页或系统应用，没有本地文件位置。");
        var target = Uri.TryCreate(raw, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : raw;
        target = Environment.ExpandEnvironmentVariables(target);
        if (!Path.IsPathFullyQualified(target)) target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, target);
        target = Path.GetFullPath(target);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new FileNotFoundException("快捷方式的目标已不存在。", target);
        return target;
    }

    internal static ProcessStartInfo CreateRevealStartInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            };
    }
}
