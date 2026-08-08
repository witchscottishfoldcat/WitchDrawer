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
            try
            {
                CopyDirectory(sourcePath, destinationPath, cancellationToken);
            }
            catch
            {
                // 复制中途失败（磁盘满/取消/无权限）时清掉半成品目录树，
                // 与文件分支和删除失败分支的回滚行为保持一致。
                TryDeleteDirectory(destinationPath);
                throw;
            }

            try
            {
                // 只读文件会挡住 Directory.Delete（UnauthorizedAccessException），
                // 跨卷移动必须像资源管理器一样先清只读位再删；副本仍保留只读属性。
                ClearReadOnlyAttributesRecursively(sourcePath);
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
            // File.Copy 会把只读位带到副本上；File.Delete 拒绝只读文件，
            // 只清源文件的只读位（源随即被删，副本保持只读不变）。
            ClearReadOnlyAttribute(sourcePath);
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

    /// <summary>
    /// 清除单个文件的只读位；交接点/符号链接跳过（链接目标不属于被移动的树）。
    /// </summary>
    private static void ClearReadOnlyAttribute(string filePath)
    {
        var attributes = File.GetAttributes(filePath);
        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// 递归清除目录树内所有文件的只读位。遍历方式与 <see cref="CopyDirectory"/>
    /// 保持一致，但不跟进交接点目录，避免改动链接目标里的文件。
    /// </summary>
    private static void ClearReadOnlyAttributesRecursively(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath))
        {
            ClearReadOnlyAttribute(file);
        }

        foreach (var directory in Directory.GetDirectories(directoryPath))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            ClearReadOnlyAttributesRecursively(directory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // 副本可能继承了只读位，回滚删除前同样要清掉。
                ClearReadOnlyAttribute(path);
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
                ClearReadOnlyAttributesRecursively(path);
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort rollback only.
        }
    }
}
