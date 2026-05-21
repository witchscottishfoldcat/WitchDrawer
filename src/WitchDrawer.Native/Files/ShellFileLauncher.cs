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
}

