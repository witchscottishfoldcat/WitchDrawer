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

        ValidateEntryIsNotReparsePoint(sourcePath);

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
        ValidateEntryIsNotReparsePoint(sourcePath);
        ValidateDestinationParent(Path.GetDirectoryName(destinationPath));

        if (isDirectory && IsSameOrDescendant(destinationPath, sourcePath))
        {
            throw new InvalidOperationException("A directory cannot be moved into itself.");
        }

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException($"Destination already exists: {destinationPath}");
        }

        // Files copy directly to their final name: small desktop exports (.lnk, documents)
        // then appear in Explorer the moment the bytes land, instead of being hidden behind a
        // ".name.witchdrawer-{guid}.tmp" staging file that only renames to the real name at the
        // very end. Directories still stage atomically because recursive tree promotion is more
        // involved and is not on the interactive export hot path.
        if (!isDirectory)
        {
            CopyFileThenDelete(sourcePath, destinationPath, cancellationToken);
            return;
        }

        var sourceSnapshot = CaptureDirectorySnapshot(sourcePath, cancellationToken);
        var stagingPath = CreateStagingPath(destinationPath);
        string? heldSourcePath = null;
        var sourceMovedToHolding = false;
        try
        {
            CopyDirectory(sourcePath, stagingPath, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var sourceAfterCopy = CaptureDirectorySnapshot(sourcePath, cancellationToken);
            EnsureSourceUnchanged(sourceSnapshot, sourceAfterCopy);
            EnsureDirectoryCopyComplete(sourceAfterCopy, CaptureDirectorySnapshot(stagingPath, cancellationToken));

            // Rename on the source volume first. New files created at the original path after
            // this point belong to a new directory and must never be consumed by this move.
            heldSourcePath = CreateHeldSourcePath(sourcePath);
            Directory.Move(sourcePath, heldSourcePath);
            sourceMovedToHolding = true;

            var heldSourceSnapshot = CaptureDirectorySnapshot(heldSourcePath, cancellationToken);
            EnsureSourceUnchanged(sourceAfterCopy, heldSourceSnapshot);

            // Promotion is an atomic same-volume rename on the destination. The complete copy
            // becomes durable before any source entry is removed.
            Directory.Move(stagingPath, destinationPath);

            try
            {
                DeleteVerifiedSourceTree(heldSourcePath, heldSourceSnapshot);
            }
            catch
            {
                // The destination already contains the verified complete tree. Put any changed
                // or undeletable remnants back at the original path when possible; preserving a
                // duplicate is safer than deleting data that arrived during the move.
                TryMoveBack(heldSourcePath, sourcePath, isDirectory: true);
            }
        }
        catch
        {
            RestoreHeldSourceOrPreserveStaging(
                sourcePath,
                heldSourcePath,
                stagingPath,
                sourceMovedToHolding);

            throw;
        }
    }

    private static void CopyFileThenDelete(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Copy straight to the final destination. If a concurrent writer creates the target
        // between our existence check and the copy, File.Copy(overwrite:false) surfaces that
        // as IOException, which the caller is expected to handle.
        File.Copy(sourcePath, destinationPath, overwrite: false);

        cancellationToken.ThrowIfCancellationRequested();
        var sourceAttributes = CaptureAttributes(sourcePath, isDirectory: false);
        try
        {
            ClearReadOnlyAttributes(sourcePath, isDirectory: false);
            File.Delete(sourcePath);
        }
        catch
        {
            RestoreAttributes(sourceAttributes);

            // The destination already holds a valid copy; roll it back so the user is not left
            // with a duplicate when the source could not be removed.
            TryDelete(destinationPath, isDirectory: false);
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

    private static string CreateHeldSourcePath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Source directory is unavailable.");
        return Path.Combine(
            directory,
            $".{Path.GetFileName(sourcePath)}.witchdrawer-{Guid.NewGuid():N}.moving");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        ValidateEntryIsNotReparsePoint(sourceDir);
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntryIsNotReparsePoint(file);
            var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntryIsNotReparsePoint(directory);
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, targetSubDir, cancellationToken);
        }
    }

    private static DirectorySnapshot CaptureDirectorySnapshot(
        string rootPath,
        CancellationToken cancellationToken)
    {
        ValidateEntryIsNotReparsePoint(rootPath);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Reparse points are not supported: {entry}");
            }

            var relativePath = Path.GetRelativePath(rootPath, entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                directories.Add(relativePath);
                continue;
            }

            var fileInfo = new FileInfo(entry);
            files.Add(
                relativePath,
                new FileSnapshot(fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }

        return new DirectorySnapshot(files, directories);
    }

    private static void EnsureSourceUnchanged(DirectorySnapshot before, DirectorySnapshot after)
    {
        if (!before.Directories.SetEquals(after.Directories)
            || before.Files.Count != after.Files.Count
            || before.Files.Any(pair =>
                !after.Files.TryGetValue(pair.Key, out var current)
                || current != pair.Value))
        {
            throw new IOException(
                "Source directory changed while it was being moved. No source files were deleted.");
        }
    }

    private static void EnsureDirectoryCopyComplete(DirectorySnapshot source, DirectorySnapshot copy)
    {
        if (!source.Directories.SetEquals(copy.Directories)
            || source.Files.Count != copy.Files.Count
            || source.Files.Any(pair =>
                !copy.Files.TryGetValue(pair.Key, out var copied)
                || copied.Length != pair.Value.Length))
        {
            throw new IOException(
                "Directory copy verification failed. No source files were deleted.");
        }
    }

    private static void DeleteVerifiedSourceTree(string rootPath, DirectorySnapshot snapshot)
    {
        foreach (var pair in snapshot.Files)
        {
            var filePath = Path.Combine(rootPath, pair.Key);
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists
                || fileInfo.Length != pair.Value.Length
                || fileInfo.LastWriteTimeUtc != pair.Value.LastWriteTimeUtc)
            {
                throw new IOException($"Source file changed while it was being moved: {filePath}");
            }

            ClearReadOnlyAttribute(filePath);
            File.Delete(filePath);
        }

        foreach (var relativePath in snapshot.Directories.OrderByDescending(GetPathDepth))
        {
            var directoryPath = Path.Combine(rootPath, relativePath);
            ClearReadOnlyAttribute(directoryPath);
            Directory.Delete(directoryPath, recursive: false);
        }

        ClearReadOnlyAttribute(rootPath);
        Directory.Delete(rootPath, recursive: false);
    }

    private static int GetPathDepth(string path)
    {
        return path.Count(character =>
            character == Path.DirectorySeparatorChar
            || character == Path.AltDirectorySeparatorChar);
    }

    private sealed record FileSnapshot(long Length, DateTime LastWriteTimeUtc);

    private sealed record DirectorySnapshot(
        IReadOnlyDictionary<string, FileSnapshot> Files,
        HashSet<string> Directories);

    internal static bool RestoreHeldSourceOrPreserveStaging(
        string sourcePath,
        string? heldSourcePath,
        string stagingPath,
        bool sourceMovedToHolding)
    {
        var sourceRestored = !sourceMovedToHolding;
        if (sourceMovedToHolding
            && !string.IsNullOrWhiteSpace(heldSourcePath)
            && Directory.Exists(heldSourcePath))
        {
            sourceRestored = TryMoveBack(heldSourcePath, sourcePath, isDirectory: true);
        }

        // If the held source cannot be returned (for example another process recreated the
        // original path), keep the verified staging copy as a second recovery path. Never
        // clean both locations when neither copy is available at the original path.
        if (sourceRestored)
        {
            TryDelete(stagingPath, isDirectory: true);
        }

        return sourceRestored;
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

    private static void ValidateEntryIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Reparse points are not supported: {path}");
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

    private static bool TryMoveBack(string source, string destination, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                if (Directory.Exists(source) && !Directory.Exists(destination) && !File.Exists(destination))
                {
                    Directory.Move(source, destination);
                    return true;
                }
            }
            else if (File.Exists(source) && !File.Exists(destination) && !Directory.Exists(destination))
            {
                File.Move(source, destination);
                return true;
            }
        }
        catch
        {
        }

        return false;
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
