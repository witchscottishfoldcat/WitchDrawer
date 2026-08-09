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
        ValidateNoReparsePoints(sourcePath, isDirectory);
        ValidateDestinationParent(Path.GetDirectoryName(destinationPath));

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (isDirectory && IsSameOrDescendant(destinationPath, sourcePath))
        {
            throw new InvalidOperationException("A directory cannot be moved into itself.");
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
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        ValidateNoReparsePoints(sourcePath, isDirectory);
        ValidateDestinationParent(Path.GetDirectoryName(destinationPath));

        if (isDirectory && IsSameOrDescendant(destinationPath, sourcePath))
        {
            throw new InvalidOperationException("A directory cannot be moved into itself.");
        }

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException($"Destination already exists: {destinationPath}");
        }

        var stagingPath = CreateStagingPath(destinationPath);
        try
        {
            if (isDirectory)
            {
                CopyDirectory(sourcePath, stagingPath, cancellationToken);
            }
            else
            {
                File.Copy(sourcePath, stagingPath, overwrite: false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sourceAttributes = CaptureAttributes(sourcePath, isDirectory);
            try
            {
                ClearReadOnlyAttributes(sourcePath, isDirectory);
                if (isDirectory)
                {
                    Directory.Delete(sourcePath, recursive: true);
                }
                else
                {
                    File.Delete(sourcePath);
                }
            }
            catch
            {
                RestoreAttributes(sourceAttributes);
                throw;
            }

            try
            {
                if (isDirectory)
                {
                    Directory.Move(stagingPath, destinationPath);
                }
                else
                {
                    File.Move(stagingPath, destinationPath);
                }
            }
            catch
            {
                // Never remove the final destination here: another operation may own it.
                // Best effort restore keeps the operation atomic when the target is free.
                TryMoveBack(stagingPath, sourcePath, isDirectory);
                throw;
            }
        }
        catch
        {
            TryDelete(stagingPath, isDirectory);
            throw;
        }
    }

    private static string CreateStagingPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination directory is unavailable.");
        return Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.witchdrawer-{Guid.NewGuid():N}.tmp");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        ValidateNoReparsePoints(sourceDir, isDirectory: true);
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateNoReparsePoints(file, isDirectory: false);
            var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateNoReparsePoints(directory, isDirectory: true);
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, targetSubDir, cancellationToken);
        }
    }

    private static bool IsSameOrDescendant(string candidate, string ancestor)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullAncestor = Path.GetFullPath(ancestor)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullAncestor, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullAncestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDestinationParent(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Destination directory is unavailable.");
        }

        var current = Path.GetFullPath(directory);
        while (!Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = parent;
        }

        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Destination path contains an unsupported reparse point: {current}");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static void ValidateNoReparsePoints(string path, bool isDirectory)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Reparse points are not supported: {path}");
        }

        if (!isDirectory)
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            ValidateNoReparsePoints(entry, Directory.Exists(entry));
        }
    }

    private static Dictionary<string, FileAttributes> CaptureAttributes(string path, bool isDirectory)
    {
        var entries = isDirectory
            ? Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Prepend(path)
            : new[] { path };
        return entries
            .Where(entry => (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
            .ToDictionary(entry => entry, File.GetAttributes, StringComparer.OrdinalIgnoreCase);
    }

    private static void ClearReadOnlyAttributes(string path, bool isDirectory)
    {
        if (!isDirectory)
        {
            ClearReadOnlyAttribute(path);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Prepend(path))
        {
            ClearReadOnlyAttribute(entry);
        }
    }

    private static void ClearReadOnlyAttribute(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static void RestoreAttributes(Dictionary<string, FileAttributes> attributes)
    {
        foreach (var pair in attributes)
        {
            RestoreAttribute(pair.Key, pair.Value);
        }
    }

    private static void RestoreAttribute(string path, FileAttributes attributes)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                File.SetAttributes(path, attributes);
            }
        }
        catch
        {
        }
    }

    private static void TryMoveBack(string source, string destination, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                if (Directory.Exists(source) && !Directory.Exists(destination) && !File.Exists(destination))
                {
                    Directory.Move(source, destination);
                }
            }
            else if (File.Exists(source) && !File.Exists(destination) && !Directory.Exists(destination))
            {
                File.Move(source, destination);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    ClearReadOnlyAttributes(path, isDirectory: true);
                    Directory.Delete(path, recursive: true);
                }
            }
            else if (File.Exists(path))
            {
                ClearReadOnlyAttribute(path);
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
