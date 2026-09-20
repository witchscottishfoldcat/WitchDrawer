using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

/// <summary>
/// 数据目录迁移：把数据库、收纳盒文件与日志整体复制到新目录，
/// 成功后写入引导配置，应用下次启动时使用新目录。
/// </summary>
public sealed class DataStorageMigrationService
{
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly StorageLocationStore _locationStore;

    public DataStorageMigrationService(
        AppPaths paths,
        DrawerRepository repository,
        StorageLocationStore locationStore)
    {
        _paths = paths;
        _repository = repository;
        _locationStore = locationStore;
    }

    /// <summary>
    /// 当前生效的数据根目录。
    /// </summary>
    public string CurrentRootDirectory => _paths.RootDirectory;

    /// <summary>
    /// 将当前数据目录整体迁移到 <paramref name="targetRootDirectory"/>。
    /// 目标目录必须为空（或不存在），且不能位于当前数据目录内部。
    /// 文件先落在临时目录，数据库通过 SQLite 备份生成一致快照，再一次性改名到位。
    /// 提升前失败可重试；提升后若引导配置未写完，下次启动根据迁移标记完成切换。
    /// 旧目录保留作为备份，由用户自行清理。
    /// </summary>
    public async Task<AppPaths> MigrateAsync(
        string targetRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRootDirectory);

        var sourceRoot = Path.GetFullPath(_paths.RootDirectory);
        var targetRoot = Path.GetFullPath(targetRootDirectory.Trim());

        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标文件夹与当前数据目录相同，无需迁移。");
        }

        if (IsDescendantOf(targetRoot, sourceRoot))
        {
            throw new InvalidOperationException("目标文件夹不能位于当前数据目录内部。");
        }

        var targetParent = Path.GetDirectoryName(targetRoot)
            ?? throw new InvalidOperationException("目标文件夹的父目录不可用。");
        Directory.CreateDirectory(targetParent);
        var lockPath = targetRoot + ".migration.lock";
        var tempRoot = targetRoot + $".tmp-migrating-{Guid.NewGuid():N}";
        var migrationId = Guid.NewGuid();
        var targetPromoted = false;

        await using var migrationLock = await AcquireMigrationLockAsync(lockPath, cancellationToken);
        try
        {
            if (Directory.Exists(targetRoot))
            {
                EnsureNoReparsePoint(targetRoot);
            }

            if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new InvalidOperationException(
                    "目标文件夹不为空。为避免覆盖已有数据，请选择一个空文件夹。");
            }

            // 每次迁移使用独占 staging，避免清理或提升另一进程的临时目录。
            var tempPaths = new AppPaths(tempRoot);
            tempPaths.EnsureCreatedAndWritable();
            DirectorySnapshot? boxesBeforeCopy = null;

            await Task.Run(() => _repository.CopyConsistentDatabaseAsync(
                tempPaths.DatabasePath,
                () =>
                {
                    boxesBeforeCopy = CaptureDirectorySnapshot(_paths.BoxesDirectory, cancellationToken);
                    CopyDirectory(sourceRoot, tempRoot, cancellationToken, isRoot: true);
                    EnsureMatchingSnapshot(
                        boxesBeforeCopy,
                        CaptureDirectorySnapshot(tempPaths.BoxesDirectory, cancellationToken),
                        compareWriteTimes: false);
                    return Task.CompletedTask;
                },
                () =>
                {
                    EnsureMatchingSnapshot(
                        boxesBeforeCopy ?? throw new InvalidOperationException("迁移文件快照不可用。"),
                        CaptureDirectorySnapshot(_paths.BoxesDirectory, cancellationToken),
                        compareWriteTimes: true);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (!File.Exists(tempPaths.DatabasePath))
                    {
                        throw new InvalidOperationException("迁移失败：数据库文件未能复制到目标文件夹。");
                    }

                    File.WriteAllText(
                        Path.Combine(tempRoot, StorageLocationStore.MigrationMarkerFileName),
                        migrationId.ToString("N"));
                    _locationStore.SaveMigrationIntent(targetRoot, migrationId);
                    PromoteStagedDirectory(tempRoot, targetRoot);
                    targetPromoted = true;
                    _repository.MarkMigrationPromoted();
                    try
                    {
                        _locationStore.SaveConfiguredDirectory(targetRoot);
                        _locationStore.ClearMigrationIntent(migrationId);
                        StorageLocationStore.TryDeleteMigrationMarker(targetRoot);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The durable intent selects the promoted snapshot on restart.
                        // Keep it and commit the old database's write barrier.
                    }
                    return Task.CompletedTask;
                },
                cancellationToken, targetRoot), cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            if (!targetPromoted)
            {
                _locationStore.ClearMigrationIntent(migrationId);
            }
            throw;
        }

        return new AppPaths(targetRoot);
    }

    private static async Task<FileStream> AcquireMigrationLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private static bool IsDescendantOf(string candidate, string ancestor)
    {
        var prefix = ancestor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken,
        bool isRoot = false)
    {
        EnsureNoReparsePoint(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(directory);
            var name = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(targetDirectory, name), cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(file);
            var name = Path.GetFileName(file);
            // 引导配置只应保留在默认目录，不随数据复制到新目录。
            if (isRoot && string.Equals(name, StorageLocationStore.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // SQLite's backup API writes a consistent database snapshot after other files
            // have been copied. WAL and SHM are live sidecars, never migration payloads.
            if (isRoot && (string.Equals(name, AppPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, AppPaths.DatabaseFileName + "-wal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, AppPaths.DatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            File.Copy(file, Path.Combine(targetDirectory, name), overwrite: false);
        }
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"迁移不支持链接或其他 reparse point: {path}");
        }
    }

    private static DirectorySnapshot CaptureDirectorySnapshot(
        string root,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoint(root);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(entry);
            var relativePath = Path.GetRelativePath(root, entry);
            if (Directory.Exists(entry))
            {
                directories.Add(relativePath);
            }
            else
            {
                var info = new FileInfo(entry);
                files.Add(relativePath, new FileSnapshot(info.Length, info.LastWriteTimeUtc));
            }
        }

        return new DirectorySnapshot(files, directories);
    }

    private static void EnsureMatchingSnapshot(
        DirectorySnapshot expected,
        DirectorySnapshot actual,
        bool compareWriteTimes)
    {
        if (!expected.Directories.SetEquals(actual.Directories)
            || expected.Files.Count != actual.Files.Count
            || expected.Files.Any(pair =>
                !actual.Files.TryGetValue(pair.Key, out var current)
                || pair.Value.Length != current.Length
                || (compareWriteTimes && pair.Value.LastWriteTimeUtc != current.LastWriteTimeUtc)))
        {
            throw new IOException("收纳盒文件在迁移期间发生变化，目标目录未启用。请重试。");
        }
    }

    private sealed record FileSnapshot(long Length, DateTime LastWriteTimeUtc);
    private sealed record DirectorySnapshot(
        IReadOnlyDictionary<string, FileSnapshot> Files,
        HashSet<string> Directories);

    internal static void PromoteStagedDirectory(string tempRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            EnsureNoReparsePoint(targetRoot);
            if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new InvalidOperationException(
                    "The migration target changed while data was being copied. Existing files were preserved.");
            }

            // Never recursively delete a user-selected target. A file can arrive after the
            // emptiness check; non-recursive deletion then fails safely instead of erasing it.
            Directory.Delete(targetRoot, recursive: false);
        }

        Directory.Move(tempRoot, targetRoot);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 尽力清理；残留由下次迁移的开头清理兜底。
        }
    }
}
