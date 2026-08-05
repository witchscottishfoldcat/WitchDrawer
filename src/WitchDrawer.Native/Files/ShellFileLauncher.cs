using System.Diagnostics;
using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Files;

public sealed class ShellFileLauncher : IFileLauncher
{
    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Cannot open a missing file or directory.", path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    public Task OpenAsAdminAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Cannot open a missing file or directory.", path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas"
        });

        return Task.CompletedTask;
    }

    public Task ShowInFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Cannot reveal a missing file or directory.", path);
        }

        // explorer.exe /select,"<path>" opens the containing folder and highlights
        // the item without navigating the folder first.
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
