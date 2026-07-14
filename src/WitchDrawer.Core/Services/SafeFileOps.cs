namespace WitchDrawer.Core.Services;

/// <summary>
/// Moves files/directories with a cross-volume fallback (copy then delete).
/// </summary>
internal static class SafeFileOps
{
    public static Task MoveAsync(
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Move(sourcePath, destinationPath, isDirectory, cancellationToken),
            cancellationToken);
    }

    public static void Move(
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (isDirectory)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {sourcePath}");
            }
        }
        else if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file does not exist.", sourcePath);
        }

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException($"Destination already exists: {destinationPath}");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (AreSameVolume(sourcePath, destinationPath))
        {
            try
            {
                if (isDirectory)
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                return;
            }
            catch (IOException)
            {
                // Fall through to copy+delete (cross-volume rename, locked intermediate paths, etc.).
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        CopyThenDelete(sourcePath, destinationPath, isDirectory, cancellationToken);
    }

    internal static bool AreSameVolume(string pathA, string pathB)
    {
        var rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
        var rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
        if (string.IsNullOrEmpty(rootA) || string.IsNullOrEmpty(rootB))
        {
            return false;
        }

        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    internal static void CopyThenDelete(
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (isDirectory)
        {
            CopyDirectory(sourcePath, destinationPath, cancellationToken);
            try
            {
                Directory.Delete(sourcePath, recursive: true);
            }
            catch
            {
                TryDeleteDirectory(destinationPath);
                throw;
            }

            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: false);
        try
        {
            File.Delete(sourcePath);
        }
        catch
        {
            TryDeleteFile(destinationPath);
            throw;
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, targetSubDir, cancellationToken);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort rollback only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort rollback only.
        }
    }
}
