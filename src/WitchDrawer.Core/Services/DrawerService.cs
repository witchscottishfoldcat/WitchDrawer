using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;
using Microsoft.Data.Sqlite;

namespace WitchDrawer.Core.Services;

public sealed class DrawerService
{
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly List<string> _recoveryWarnings = [];
    private volatile bool _retryPendingRecoveryOnRead;
    private readonly SemaphoreSlim _fileOperationGate = new(1, 1);
    private readonly SemaphoreSlim _settingsWriteGate = new(1, 1);

    // Exposed so tests can simulate a live journaled operation holding the gate.
    internal SemaphoreSlim FileOperationGate => _fileOperationGate;

    public IReadOnlyList<string> RecoveryWarnings => _recoveryWarnings;

    public DrawerService(AppPaths paths, DrawerRepository repository)
    {
        _paths = paths;
        _repository = repository;
    }

    /// <summary>
    /// 启动初始化整体在后台线程执行：SQLite 的异步 API 可能同步完成，
    /// 恢复日志扫描与路径修复中的文件存在检查都不应占用界面线程。
    /// 顺序必须保持：恢复未完成操作 → 修复存储路径 → 默认盒子检查。
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => InitializeCoreAsync(cancellationToken), cancellationToken);

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        await _repository.InitializeAsync(cancellationToken);
        await RecoverPendingFileOperationsAsync(cancellationToken);
        await RepairStoredPathsAsync(cancellationToken);
        await EnsureDefaultBoxesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _repository.GetBoxesAsync(cancellationToken), cancellationToken);
    }

    public Task ReorderBoxesAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedBoxIds);
        var requestedIds = orderedBoxIds.ToArray();
        return Task.Run(() => ReorderBoxesCoreAsync(requestedIds, cancellationToken), cancellationToken);
    }

    private async Task ReorderBoxesCoreAsync(Guid[] orderedBoxIds, CancellationToken cancellationToken)
    {
        if (orderedBoxIds.Distinct().Count() != orderedBoxIds.Length)
        {
            throw new ArgumentException("Box order cannot contain duplicate ids.", nameof(orderedBoxIds));
        }

        var existingBoxes = await _repository.GetBoxesAsync(cancellationToken);
        var existingIds = existingBoxes.Select(box => box.Id).ToHashSet();
        if (orderedBoxIds.Length != existingIds.Count || orderedBoxIds.Any(id => !existingIds.Contains(id)))
        {
            throw new ArgumentException(
                "Box order must contain every existing box exactly once.",
                nameof(orderedBoxIds));
        }

        await _repository.UpdateBoxSortOrdersAsync(orderedBoxIds, cancellationToken);
    }

    // SQLite's async APIs can execute synchronously. Offload the entire operation,
    // including its first query and path checks, before returning to WPF callers.
    public Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid boxId, CancellationToken cancellationToken = default)
        => Task.Run(() => GetItemsCoreAsync(boxId, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<DrawerItem>> GetItemsCoreAsync(Guid boxId, CancellationToken cancellationToken)
    {
        await RetryPendingRecoveryOnReadAsync(cancellationToken);
        await PruneMissingStoredItemsAsync(boxId, cancellationToken);
        return await _repository.GetItemsAsync(boxId, cancellationToken);
    }

    public Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => GetAllItemsCoreAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<DrawerItem>> GetAllItemsCoreAsync(CancellationToken cancellationToken)
    {
        await RetryPendingRecoveryOnReadAsync(cancellationToken);
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.GetItemsAsync(null, cancellationToken);
    }

    public Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
        => Task.Run(() => SearchItemsCoreAsync(query, limit, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<DrawerItem>> SearchItemsCoreAsync(string query, int limit, CancellationToken cancellationToken)
    {
        await RetryPendingRecoveryOnReadAsync(cancellationToken);
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.SearchItemsAsync(query.Trim(), limit, cancellationToken);
    }

    public Task<Box> CreateBoxAsync(string name, BoxType type, CancellationToken cancellationToken = default)
        => Task.Run(() => CreateBoxCoreAsync(name, type, cancellationToken), cancellationToken);

    private async Task<Box> CreateBoxCoreAsync(string name, BoxType type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(name));
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var storagePath = type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer
            ? Path.Combine(_paths.BoxesDirectory, id.ToString("N"))
            : null;
        if (storagePath is not null)
        {
            Directory.CreateDirectory(storagePath);
        }

        var box = new Box(
            id,
            name.Trim(),
            type,
            storagePath,
            await _repository.GetNextBoxSortOrderAsync(cancellationToken),
            now,
            now);

        await _repository.AddBoxAsync(box, cancellationToken);
        return box;
    }

    public Task<DrawerItem> ImportPathAsync(
        Guid boxId,
        string sourcePath,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ImportPathCoreAsync(boxId, sourcePath, gridColumn, gridRow, cancellationToken), cancellationToken);

    private async Task<DrawerItem> ImportPathCoreAsync(
        Guid boxId, string sourcePath, int? gridColumn, int? gridRow, CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        if (box.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Todo boxes do not accept files.");
        }

        if (box.Type != BoxType.Mapping)
        {
            ValidateImportSource(Path.GetFullPath(sourcePath));
        }
        var fullSourcePath = PathSafety.GetFullExistingPath(sourcePath);
        var isDirectory = Directory.Exists(fullSourcePath);
        var itemKind = isDirectory ? ItemKind.Directory : ItemKind.File;
        var displayName = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sortOrder = await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        DrawerItem item;
        if (box.Type == BoxType.Mapping)
        {
            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                displayName,
                itemKind,
                fullSourcePath,
                null,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);
        }
        else
        {
            var storageRoot = box.StoragePath ?? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            Directory.CreateDirectory(storageRoot);
            var targetPath = Path.Combine(storageRoot, displayName);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                Path.GetFileName(targetPath),
                itemKind,
                fullSourcePath,
                targetPath,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);

            var operation = new PendingFileOperation(
                Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
                fullSourcePath, targetPath, isDirectory, item);
            var completed = await ExecuteJournaledOperationAsync(operation, cancellationToken);

            return completed.ResultItem!;
        }

        await _repository.AddItemAsync(item, cancellationToken);
        return item;
    }

    public Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _repository.UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken), cancellationToken);
    }

    public Task UpdateItemGridPositionsAsync(
        IReadOnlyDictionary<Guid, (int GridColumn, int GridRow)> positions,
        CancellationToken cancellationToken = default)
    {
        var snapshot = positions.ToDictionary(entry => entry.Key, entry => entry.Value);
        return Task.Run(() => _repository.UpdateItemGridPositionsAsync(snapshot, cancellationToken), cancellationToken);
    }

    public Task MoveItemToBoxAsync(
        Guid itemId,
        Guid targetBoxId,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => MoveItemToBoxCoreAsync(itemId, targetBoxId, gridColumn, gridRow, cancellationToken), cancellationToken);

    private async Task MoveItemToBoxCoreAsync(
        Guid itemId, Guid targetBoxId, int? gridColumn, int? gridRow, CancellationToken cancellationToken)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");
        var sourceBox = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        var targetBox = await _repository.GetBoxAsync(targetBoxId, cancellationToken)
            ?? throw new InvalidOperationException("Target box does not exist.");

        if (sourceBox.Type == BoxType.Todo || targetBox.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Files cannot be moved into or out of a todo box.");
        }

        if (item.BoxId == targetBoxId)
        {
            await UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
            return;
        }

        var targetSortOrder = await _repository.GetNextItemSortOrderAsync(targetBoxId, cancellationToken);
        var sourcePath = item.SourcePath;
        var storedPath = item.StoredPath;
        var displayName = item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;

        if (targetBox.Type == BoxType.Mapping)
        {
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                throw new InvalidOperationException("Stored items cannot be moved into a mapping box.");
            }

            storedPath = null;
        }
        else
        {
            if (sourceBox.Type == BoxType.Mapping)
            {
                throw new InvalidOperationException("Mapping references cannot be moved into a storage box.");
            }

            var sourceFilePath = item.EffectivePath;
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new InvalidOperationException("Item has no file path.");
            }

            var fullSourcePath = PathSafety.GetFullExistingPath(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullSourcePath);
            }

            var storageRoot = targetBox.StoragePath ?? Path.Combine(_paths.BoxesDirectory, targetBox.Id.ToString("N"));
            Directory.CreateDirectory(storageRoot);
            var targetPath = Path.Combine(storageRoot, displayName);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            displayName = Path.GetFileName(targetPath);
            storedPath = targetPath;

            var resultItem = item with
            {
                BoxId = targetBox.Id,
                DisplayName = displayName,
                StoredPath = storedPath,
                SortOrder = targetSortOrder,
                GridColumn = gridColumn,
                GridRow = gridRow
            };
            var operation = new PendingFileOperation(
                Guid.NewGuid(), PendingFileOperationKind.Move, item.Id,
                fullSourcePath, targetPath, isDirectory, resultItem);
            await ExecuteJournaledOperationAsync(operation, cancellationToken);

            return;
        }

        await _repository.MoveItemToBoxAsync(
            item,
            targetBox.Id,
            displayName,
            sourcePath,
            storedPath,
            targetSortOrder,
            gridColumn,
            gridRow,
            cancellationToken);
    }

    public Task<string> ExportItemToDirectoryAsync(
        Guid itemId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ExportItemToDirectoryCoreAsync(itemId, targetDirectory, cancellationToken), cancellationToken);

    private async Task<string> ExportItemToDirectoryCoreAsync(
        Guid itemId, string targetDirectory, CancellationToken cancellationToken)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Only stored items can be exported.");
        }

        var sourcePath = PathSafety.GetFullExistingPath(item.StoredPath);
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, sourcePath);

        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(fullTargetDirectory);

        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? Path.GetFileName(sourcePath)
            : item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;
        var targetPath = Path.Combine(fullTargetDirectory, displayName);
        PathSafety.EnsureChildPath(fullTargetDirectory, targetPath);

        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Remove, itemId,
            sourcePath, targetPath, isDirectory, null);
        var completed = await ExecuteJournaledOperationAsync(operation, cancellationToken);

        return completed.TargetPath;
    }

    public Task<ItemDeleteResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => Task.Run(() => DeleteItemCoreAsync(itemId, cancellationToken), cancellationToken);

    private async Task<ItemDeleteResult> DeleteItemCoreAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
            return ItemDeleteResult.ReferenceRemoved(item.Id, item.DisplayName);
        }

        return await RestoreAndRemoveStoredItemAsync(item, reservedTargets: null, cancellationToken);
    }

    public Task<BoxDeleteResult> DeleteBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
        => Task.Run(() => DeleteBoxWithGateAsync(boxId, cancellationToken), cancellationToken);

    private async Task<BoxDeleteResult> DeleteBoxWithGateAsync(Guid boxId, CancellationToken cancellationToken)
    {
        await _fileOperationGate.WaitAsync(cancellationToken);
        try
        {
            return await DeleteBoxCoreAsync(boxId, cancellationToken);
        }
        finally
        {
            _fileOperationGate.Release();
        }
    }

    private async Task<BoxDeleteResult> DeleteBoxCoreAsync(Guid boxId, CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        if (box.Type is BoxType.Mapping or BoxType.Todo)
        {
            await _repository.RemoveBoxAsync(boxId, cancellationToken);
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: true,
                RestoredCount: 0,
                FailedCount: 0,
                Failures: Array.Empty<string>());
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var itemIds = items.Select(item => item.Id).ToHashSet();
        var pending = await _repository.GetPendingFileOperationsAsync(cancellationToken);
        if (pending.Any(operation => operation.ResultItem?.BoxId == boxId || itemIds.Contains(operation.ItemId)))
        {
            throw new InvalidOperationException("盒子包含待恢复的文件操作，请完成恢复后再删除。");
        }
        var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var restoredCount = 0;
        var failures = new List<string>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StoredPath))
            {
                await _repository.RemoveItemAsync(item.Id, cancellationToken);
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RestoreAndRemoveStoredItemAsync(item, reservedTargets, cancellationToken, fileOperationGateHeld: true);
                restoredCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add($"{item.DisplayName}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: false,
                RestoredCount: restoredCount,
                FailedCount: failures.Count,
                Failures: failures);
        }

        await _repository.RemoveBoxAsync(boxId, cancellationToken);
        TryDeleteBoxStorageDirectory(box);

        return new BoxDeleteResult(
            box.Id,
            box.Name,
            box.Type,
            BoxRemoved: true,
            RestoredCount: restoredCount,
            FailedCount: 0,
            Failures: Array.Empty<string>());
    }

    private async Task<ItemDeleteResult> RestoreAndRemoveStoredItemAsync(
        DrawerItem item,
        HashSet<string>? reservedTargets,
        CancellationToken cancellationToken,
        bool fileOperationGateHeld = false)
    {
        var plan = CreateRestorePlan(item, reservedTargets);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Remove, item.Id,
            plan.SourcePath, plan.TargetPath, plan.IsDirectory, null);
        var completed = await ExecuteJournaledOperationAsync(operation, cancellationToken, fileOperationGateHeld);
        return new ItemDeleteResult(
            item.Id,
            item.DisplayName,
            WasStoredItem: true,
            RestoredPath: completed.TargetPath,
            RestoredToOriginal: plan.RestoredToOriginal,
            RestoredToDesktop: plan.RestoredToDesktop);
    }

    private RestorePlan CreateRestorePlan(DrawerItem item, HashSet<string>? reservedTargets)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Mapping items do not have stored files to restore.");
        }

        var storedPath = PathSafety.GetFullExistingPath(item.StoredPath);
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);

        var isDirectory = Directory.Exists(storedPath);
        var originalName = ResolveRestoreFileName(item, storedPath);

        if (TryGetExistingOriginalDirectory(item.SourcePath, out var originalDirectory))
        {
            var targetPath = GetReservedUniqueDestinationPath(originalDirectory, originalName, isDirectory, reservedTargets);
            PathSafety.EnsureChildPath(originalDirectory, targetPath);
            return new RestorePlan(storedPath, targetPath, isDirectory, RestoredToOriginal: true, RestoredToDesktop: false);
        }

        var desktopDirectory = GetDesktopDirectory();
        Directory.CreateDirectory(desktopDirectory);
        var desktopTarget = GetReservedUniqueDestinationPath(desktopDirectory, originalName, isDirectory, reservedTargets);
        PathSafety.EnsureChildPath(desktopDirectory, desktopTarget);
        return new RestorePlan(storedPath, desktopTarget, isDirectory, RestoredToOriginal: false, RestoredToDesktop: true);
    }

    private static string ResolveRestoreFileName(DrawerItem item, string storedPath)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            try
            {
                var originalPath = Path.GetFullPath(item.SourcePath);
                var fromSource = Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fromSource))
                {
                    return fromSource;
                }
            }
            catch
            {
                // Fall through to display name / stored path.
            }
        }

        if (!string.IsNullOrWhiteSpace(item.DisplayName))
        {
            return item.DisplayName;
        }

        var fromStored = Path.GetFileName(storedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fromStored))
        {
            throw new InvalidOperationException("Item does not contain a file name to restore.");
        }

        return fromStored;
    }

    private static bool TryGetExistingOriginalDirectory(string? sourcePath, out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            var originalPath = Path.GetFullPath(sourcePath);
            var originalDirectory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(originalDirectory) || !Directory.Exists(originalDirectory))
            {
                return false;
            }

            directory = originalDirectory;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDesktopDirectory()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            throw new InvalidOperationException("Desktop directory is not available for restore fallback.");
        }

        return Path.GetFullPath(desktopPath);
    }

    private static string GetReservedUniqueDestinationPath(
        string directory,
        string fileName,
        bool isDirectory,
        HashSet<string>? reservedTargets)
    {
        var targetPath = FileNameService.GetUniqueDestinationPath(directory, fileName, isDirectory);
        if (reservedTargets is null)
        {
            return targetPath;
        }

        var normalizedTargetPath = Path.GetFullPath(targetPath);
        if (reservedTargets.Add(normalizedTargetPath))
        {
            return targetPath;
        }

        var nameWithoutExtension = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExtension} ({index}){extension}");
            var normalizedCandidate = Path.GetFullPath(candidate);
            if ((File.Exists(candidate) || Directory.Exists(candidate))
                || !reservedTargets.Add(normalizedCandidate))
            {
                continue;
            }

            return candidate;
        }

        throw new IOException($"Could not find a unique destination for {fileName}.");
    }

    private void TryDeleteBoxStorageDirectory(Box box)
    {
        try
        {
            var storagePath = box.StoragePath;
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                storagePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            }

            var fullStoragePath = Path.GetFullPath(storagePath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullStoragePath);

            if (Directory.Exists(fullStoragePath)
                && Directory.GetFileSystemEntries(fullStoragePath).Length == 0)
            {
                Directory.Delete(fullStoragePath, recursive: false);
            }
        }
        catch
        {
            // Storage cleanup is best-effort.
        }
    }

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return _repository.GetSettingAsync(key, cancellationToken);
    }

    /// <summary>
    /// 启动时一次性读取全部设置（单连接单查询），供主窗口与桌面盒子共享启动快照。
    /// 快照仅用于本轮启动；运行期间的读取仍走 <see cref="GetSettingAsync"/>。
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        CancellationToken cancellationToken = default)
        => Task.Run(() => _repository.GetAllSettingsAsync(cancellationToken), cancellationToken);

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        // Queue before dispatching to the pool so rapid UI changes cannot persist
        // an older value after a newer one. Waiting for SQLite never blocks WPF.
        await _settingsWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => _repository.SetSettingAsync(key, value, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _settingsWriteGate.Release();
        }
    }

    public async Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await _settingsWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => _repository.DeleteSettingAsync(key, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _settingsWriteGate.Release();
        }
    }

    public Task RenameBoxAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
        => Task.Run(() => RenameBoxCoreAsync(boxId, newName, cancellationToken), cancellationToken);

    private async Task RenameBoxCoreAsync(Guid boxId, string newName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(newName));
        }

        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        await _repository.UpdateBoxNameAsync(boxId, newName.Trim(), cancellationToken);
    }

    public async Task OpenItemAsync(Guid itemId, IFileLauncher launcher, CancellationToken cancellationToken = default)
    {
        var item = await Task.Run(() => _repository.GetItemAsync(itemId, cancellationToken), cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.OpenAsync(path, cancellationToken);
    }

    private async Task PruneMissingStoredItemsAsync(Guid? boxId, CancellationToken cancellationToken)
    {
        // 存储根不可达（可移动盘/网络盘暂时掉线）时绝不能清理：
        // 文件仍然存在只是暂时不可见，把"看不到"当成"已删除"会永久销毁记录与恢复信息，
        // 驱动器重新挂载后文件就变成无人知晓的孤儿。
        if (!Directory.Exists(_paths.BoxesDirectory))
        {
            return;
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var missingItems = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items
                .Where(IsMissingStoredItem)
                .ToArray();
        }, cancellationToken);

        foreach (var item in missingItems)
        {
            await _repository.RemoveMissingItemUnlessPendingAsync(
                item.Id, item.StoredPath!, cancellationToken);
        }
    }

    private bool IsMissingStoredItem(DrawerItem item)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            return false;
        }

        try
        {
            var storedPath = Path.GetFullPath(item.StoredPath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);

            // A missing or inaccessible parent can mean an offline volume, a temporarily
            // unavailable box directory, or a stale pre-migration path. Preserve the database
            // record unless the containing directory is definitely reachable.
            var parentDirectory = Path.GetDirectoryName(storedPath);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                return false;
            }

            return !File.Exists(storedPath) && !Directory.Exists(storedPath);
        }
        catch
        {
            return false;
        }
    }

    private async Task RepairStoredPathsAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        foreach (var box in boxes.Where(box => box.Type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer))
        {
            var expectedStoragePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));

            // 先比较记录路径：路径一致（再次启动的常态）时无需为改写入库做目录存在检查；
            // 不一致时才确认期望目录确实存在（可移动盘掉线时不改写记录）。
            var storagePathMatches = string.Equals(
                Path.GetFullPath(box.StoragePath ?? expectedStoragePath),
                Path.GetFullPath(expectedStoragePath),
                StringComparison.OrdinalIgnoreCase);
            if (!storagePathMatches)
            {
                if (!Directory.Exists(expectedStoragePath))
                {
                    continue;
                }

                await _repository.UpdateBoxStoragePathAsync(
                    box.Id,
                    expectedStoragePath,
                    cancellationToken);
            }
            else if (!Directory.Exists(expectedStoragePath))
            {
                // 盒子目录不可达时无法判断条目存储路径是否需要修复。
                continue;
            }

            var items = await _repository.GetItemsAsync(box.Id, cancellationToken);
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.StoredPath)))
            {
                var name = Path.GetFileName(item.StoredPath);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var expectedStoredPath = Path.Combine(expectedStoragePath, name);
                // 路径一致是绝大多数情况：先做字符串比较，跳过文件存在检查。
                if (string.Equals(item.StoredPath, expectedStoredPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.GetFullPath(item.StoredPath!),
                        Path.GetFullPath(expectedStoredPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(expectedStoredPath) && !Directory.Exists(expectedStoredPath))
                {
                    continue;
                }

                await _repository.UpdateItemStoredPathAsync(
                    item.Id,
                    expectedStoredPath,
                    cancellationToken);
            }
        }
    }

    private async Task EnsureDefaultBoxesAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        if (boxes.Count > 0)
        {
            return;
        }

        await CreateBoxAsync("普通收纳盒", BoxType.Normal, cancellationToken);
        await CreateBoxAsync("映射收纳盒", BoxType.Mapping, cancellationToken);
    }

    private void ValidateImportSource(string sourcePath)
    {
        // Reject aliases too: a normal-looking file under a junction could be our database.
        for (var current = sourcePath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                PathSafety.EnsureNoReparsePoints(current);
            }
        }

        var bootstrap = StorageLocationStore.ForCurrentUser().FilePath;
        var protectedPaths = new[]
        {
            _paths.BoxesDirectory, _paths.LogsDirectory, _paths.DatabasePath,
            _paths.DatabasePath + "-wal", _paths.DatabasePath + "-shm", _paths.DatabasePath + "-journal",
            Path.Combine(_paths.RootDirectory, StorageLocationStore.ConfigFileName), bootstrap,
            Path.Combine(_paths.RootDirectory, StorageLocationStore.MigrationMarkerFileName)
        };
        if (protectedPaths.Any(path => IsSameOrDescendant(sourcePath, path)
                || (Directory.Exists(sourcePath) && IsSameOrDescendant(path, sourcePath)))
            || sourcePath.StartsWith(bootstrap + ".", StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith(Path.Combine(_paths.RootDirectory, StorageLocationStore.ConfigFileName) + ".",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能通过拖入搬移 WitchDrawer 数据文件或已收纳的文件；请使用盒间移动。");
        }
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PendingFileOperation> ExecuteJournaledOperationAsync(
        PendingFileOperation operation,
        CancellationToken cancellationToken,
        bool fileOperationGateHeld = false)
    {
        // Hold the gate for the whole journal → move → commit sequence. A recovery pass
        // triggered by a concurrent read must never see this live operation's journal
        // entry or staging files and mistake them for crash debris.
        if (!fileOperationGateHeld)
        {
            await _fileOperationGate.WaitAsync(cancellationToken);
        }
        try
        {
            if (operation.Kind != PendingFileOperationKind.Import)
            {
                var currentItem = await _repository.GetItemAsync(operation.ItemId, cancellationToken);
                if (currentItem is null || !string.Equals(currentItem.StoredPath, operation.SourcePath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("文件已被其他操作移动或移除，请刷新后重试。");
                }
            }
            var pending = await _repository.GetPendingFileOperationsAsync(cancellationToken);
            var reserved = pending.Select(value => Path.GetFullPath(value.TargetPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var target = await Task.Run(() => GetReservedUniqueDestinationPath(
                Path.GetDirectoryName(operation.TargetPath)!, Path.GetFileName(operation.TargetPath),
                operation.IsDirectory, reserved), cancellationToken);
            operation = operation with
            {
                TargetPath = target,
                ResultItem = operation.ResultItem is null ? null : operation.ResultItem with
                {
                    StoredPath = target,
                    DisplayName = Path.GetFileName(target)
                }
            };
            await MoveWithJournalAsync(operation, cancellationToken);
            await CompleteJournaledOperationAsync(operation);
            return operation;
        }
        finally
        {
            if (!fileOperationGateHeld)
            {
                _fileOperationGate.Release();
            }
        }
    }

    private async Task MoveWithJournalAsync(
        PendingFileOperation operation,
        CancellationToken cancellationToken)
    {
        // The durable intent must exist before the first filesystem mutation.
        await Task.Run(
            () => _repository.AddPendingFileOperationAsync(operation, cancellationToken),
            cancellationToken);
        try
        {
            await SafeFileOps.MoveAsync(
                operation.SourcePath,
                operation.TargetPath,
                operation.IsDirectory,
                cancellationToken,
                operation.Id);
        }
        catch
        {
            _retryPendingRecoveryOnRead = true;
            await TryClearAbortedOperationAsync(operation);
            throw;
        }
    }

    private async Task TryClearAbortedOperationAsync(PendingFileOperation operation)
    {
        var stage = SafeFileOps.CreateStagingPath(operation.TargetPath, operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(operation.SourcePath, operation.Id);
        if (PathExists(operation.SourcePath)
            && !PathExists(stage)
            && !PathExists(held))
        {
            await ClearPendingOperationAsync(operation.Id);
        }
    }

    internal async Task CompleteJournaledOperationAsync(PendingFileOperation operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Task.Run(() => _repository.CompletePendingFileOperationAsync(operation, CancellationToken.None));
                return;
            }
            catch (SqliteException exception) when (attempt < 2 && exception.SqliteErrorCode is 5 or 6)
            {
                await Task.Delay(100 * (attempt + 1));
            }
            catch (Exception exception)
            {
                throw await CreateCompletionFailureAsync(operation, exception);
            }
        }
    }

    private async Task<Exception> CreateCompletionFailureAsync(
        PendingFileOperation operation,
        Exception cause)
    {
        // The file move already finished but the database commit failed. Best effort: put
        // the entry back at its original path so the user sees a clean failure. The journal
        // is only cleared after the restore succeeds, so a crash or a failed restore is
        // still recoverable on the next startup or read (the file then stays at the target
        // and recovery completes the commit instead).
        if (!PathExists(operation.SourcePath)
            && PathExists(operation.TargetPath)
            && await TryMoveBackAsync(operation))
        {
            try
            {
                await ClearPendingOperationAsync(operation.Id);
            }
            catch
            {
                // The journal survives; recovery recognizes the restored state and drops it.
                _retryPendingRecoveryOnRead = true;
            }

            return new IOException("记录保存失败，文件已放回原位，请重试。", cause);
        }

        _retryPendingRecoveryOnRead = true;
        return new IOException(
            $"文件搬移记录尚未保存；下次读取或启动时会重试。请保留 {operation.TargetPath}"
            + $" 及补偿暂存文件 {SafeFileOps.CreateHeldSourcePath(operation.TargetPath, operation.Id)}、"
            + SafeFileOps.CreateStagingPath(operation.SourcePath, operation.Id),
            cause);
    }

    private async Task<bool> TryMoveBackAsync(PendingFileOperation operation)
    {
        try
        {
            // Persist the reverse direction before creating any reverse staging artifacts.
            await Task.Run(() => _repository.BeginFileOperationCompensationAsync(operation.Id));
            await SafeFileOps.MoveAsync(
                operation.TargetPath,
                operation.SourcePath,
                operation.IsDirectory,
                CancellationToken.None,
                operation.Id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RetryPendingRecoveryOnReadAsync(CancellationToken cancellationToken)
    {
        if (!_retryPendingRecoveryOnRead)
        {
            return;
        }

        await Task.Run(() => RecoverPendingFileOperationsAsync(cancellationToken), cancellationToken);
        _retryPendingRecoveryOnRead = _recoveryWarnings.Count > 0;
    }

    private async Task RecoverPendingFileOperationsAsync(CancellationToken cancellationToken)
    {
        // Serialize with ExecuteJournaledOperationAsync: recovery inspects journal entries and
        // staging/held artifacts, so it must never run while a live move is in flight.
        await _fileOperationGate.WaitAsync(cancellationToken);
        try
        {
            _recoveryWarnings.Clear();
            var operations = await _repository.GetPendingFileOperationsAsync(cancellationToken);
            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RecoverPendingFileOperationAsync(operation, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _recoveryWarnings.Add($"{operation.SourcePath} → {operation.TargetPath}: {exception.Message}");
                }
            }
            _retryPendingRecoveryOnRead = _recoveryWarnings.Count > 0;
        }
        finally
        {
            _fileOperationGate.Release();
        }
    }

    private async Task RecoverPendingFileOperationAsync(
        PendingFileOperation operation,
        CancellationToken cancellationToken)
    {
        var storedPath = operation.Kind == PendingFileOperationKind.Remove
            ? operation.SourcePath
            : operation.TargetPath;
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);

        if (operation.IsCompensating)
        {
            await RecoverCompensationAsync(operation, cancellationToken);
            return;
        }

        var stage = SafeFileOps.CreateStagingPath(operation.TargetPath, operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(operation.SourcePath, operation.Id);
        if (PathExists(held))
        {
            // A crash or cleanup failure may have left a changed or partially deleted
            // source. Do not make the destination authoritative without inspection.
            if (PathExists(operation.TargetPath))
            {
                throw new IOException($"暂存源文件仍在 {held}；目标文件也存在，需要人工核对。");
            }
        }

        var currentItem = await _repository.GetItemAsync(operation.ItemId, cancellationToken);
        var databaseCommitted = operation.Kind switch
        {
            PendingFileOperationKind.Import => currentItem is not null,
            PendingFileOperationKind.Move => currentItem is not null
                && string.Equals(currentItem.StoredPath, operation.TargetPath, StringComparison.OrdinalIgnoreCase),
            PendingFileOperationKind.Remove => currentItem is null,
            _ => throw new InvalidOperationException($"Unknown file operation: {operation.Kind}")
        };
        if (databaseCommitted)
        {
            await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
            return;
        }

        var sourceExists = PathExists(operation.SourcePath);
        var targetExists = PathExists(operation.TargetPath);
        var heldExists = PathExists(held);

        if (targetExists)
        {
            if (sourceExists)
            {
                throw new IOException(
                    $"File move recovery needs inspection: both source and target exist: {operation.SourcePath}, {operation.TargetPath}");
            }

            switch (operation.Kind)
            {
                case PendingFileOperationKind.Import:
                case PendingFileOperationKind.Move:
                case PendingFileOperationKind.Remove:
                    await _repository.CompletePendingFileOperationAsync(operation, CancellationToken.None);
                    break;
            }
            return;
        }

        if (heldExists && !sourceExists)
        {
            await Task.Run(() =>
            {
                var sourceParent = Path.GetDirectoryName(operation.SourcePath)
                    ?? throw new IOException("Original source directory is unavailable.");
                PathSafety.EnsureChildPath(sourceParent, operation.SourcePath);
                if (operation.IsDirectory)
                {
                    Directory.Move(held, operation.SourcePath);
                }
                else
                {
                    File.Move(held, operation.SourcePath);
                }
            }, cancellationToken);
            sourceExists = true;
            heldExists = false;
        }

        if (!sourceExists || heldExists)
        {
            throw new IOException(
                $"File move recovery needs inspection: source is unavailable: {operation.SourcePath}");
        }

        await Task.Run(
            () => SafeFileOps.DeleteRecoveryArtifact(stage, operation.IsDirectory),
            cancellationToken);
        await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private async Task RecoverCompensationAsync(PendingFileOperation operation, CancellationToken cancellationToken)
    {
        var stage = SafeFileOps.CreateStagingPath(operation.SourcePath, operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(operation.TargetPath, operation.Id);
        if (PathExists(operation.SourcePath))
        {
            if (PathExists(operation.TargetPath) || PathExists(held))
            {
                throw new IOException($"补偿副本需要人工核对：{operation.SourcePath}；{operation.TargetPath}；{held}");
            }
        }
        else
        {
            if (PathExists(held))
            {
                if (PathExists(operation.TargetPath))
                {
                    throw new IOException($"补偿副本需要人工核对：{operation.TargetPath}；{held}");
                }
                // Before reverse promotion, the held copy is still complete. Restore it
                // to the forward target, discard the partial reverse copy and retry.
                PathSafety.EnsureChildPath(Path.GetDirectoryName(operation.TargetPath)!, operation.TargetPath);
                if (operation.IsDirectory)
                {
                    Directory.Move(held, operation.TargetPath);
                }
                else
                {
                    File.Move(held, operation.TargetPath);
                }
            }
            if (!PathExists(operation.TargetPath))
            {
                throw new IOException($"补偿源不可用，请核对：{operation.TargetPath}；{held}；{stage}");
            }
            SafeFileOps.DeleteRecoveryArtifact(stage, operation.IsDirectory);
            await SafeFileOps.MoveAsync(operation.TargetPath, operation.SourcePath,
                operation.IsDirectory, cancellationToken, operation.Id);
        }
        SafeFileOps.DeleteRecoveryArtifact(stage, operation.IsDirectory);
        await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
    }

    private Task ClearPendingOperationAsync(Guid operationId) => Task.Run(
        () => _repository.RemovePendingFileOperationAsync(operationId, CancellationToken.None));

    private sealed record RestorePlan(
        string SourcePath,
        string TargetPath,
        bool IsDirectory,
        bool RestoredToOriginal,
        bool RestoredToDesktop);
}
