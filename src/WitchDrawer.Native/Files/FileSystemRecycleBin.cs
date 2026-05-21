using Microsoft.VisualBasic.FileIO;
using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Files;

public sealed class FileSystemRecycleBin : IFileTrash
{
    public Task MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return;
            }

            if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }, cancellationToken);
    }
}

