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
    /// 复制先落在临时目录，全部成功后一次性改名到位：失败/取消只残留临时目录，
    /// 下次重试前会被自动清理，迁移永远可重试。
    /// 迁移成功后仅更新引导配置；旧目录保留作为备份，由用户自行清理。
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

            await _repository.CheckpointAsync(cancellationToken);
            CopyDirectory(sourceRoot, tempRoot, cancellationToken);

            if (File.Exists(_paths.DatabasePath) && !File.Exists(tempPaths.DatabasePath))
            {
                throw new InvalidOperationException("迁移失败：数据库文件未能复制到目标文件夹。");
            }

            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, recursive: true);
            }

            Directory.Move(tempRoot, targetRoot);
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }

        _locationStore.SaveConfiguredDirectory(targetRoot);
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
        CancellationToken cancellationToken)
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
            if (string.Equals(name, StorageLocationStore.ConfigFileName, StringComparison.OrdinalIgnoreCase))
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
