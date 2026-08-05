using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class DrawerService
{
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;

    public DrawerService(AppPaths paths, DrawerRepository repository)
    {
        _paths = paths;
        _repository = repository;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await _repository.InitializeAsync(cancellationToken);
        await EnsureDefaultBoxesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetBoxesAsync(cancellationToken);
    }

    public async Task ReorderBoxesAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedBoxIds);

        var requestedIds = orderedBoxIds.ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new ArgumentException("Box order cannot contain duplicate ids.", nameof(orderedBoxIds));
        }

        var existingBoxes = await _repository.GetBoxesAsync(cancellationToken);
        var existingIds = existingBoxes.Select(box => box.Id).ToHashSet();
        if (requestedIds.Length != existingIds.Count || requestedIds.Any(id => !existingIds.Contains(id)))
        {
            throw new ArgumentException(
                "Box order must contain every existing box exactly once.",
                nameof(orderedBoxIds));
        }

        await _repository.UpdateBoxSortOrdersAsync(requestedIds, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        await PruneMissingStoredItemsAsync(boxId, cancellationToken);
        return await _repository.GetItemsAsync(boxId, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default)
    {
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.GetItemsAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.SearchItemsAsync(query.Trim(), limit, cancellationToken);
    }

    public async Task<Box> CreateBoxAsync(string name, BoxType type, CancellationToken cancellationToken = default)
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

    public async Task<DrawerItem> ImportPathAsync(
        Guid boxId,
        string sourcePath,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        if (box.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Todo boxes do not accept files.");
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
            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            try
            {
                await SafeFileOps.MoveAsync(fullSourcePath, targetPath, isDirectory, cancellationToken);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw CreateImportPermissionException(displayName, fullSourcePath, exception);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"无法收纳“{displayName}”：{exception.Message}",
                    exception);
            }

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

            try
            {
                await _repository.AddItemAsync(item, cancellationToken);
            }
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory, cancellationToken);
                throw;
            }

            return item;
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
        return _repository.UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
    }

    public async Task MoveItemToBoxAsync(
        Guid itemId,
        Guid targetBoxId,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
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
            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            try
            {
                await SafeFileOps.MoveAsync(fullSourcePath, targetPath, isDirectory, cancellationToken);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw CreateImportPermissionException(displayName, fullSourcePath, exception);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"无法移动“{displayName}”：{exception.Message}",
                    exception);
            }

            displayName = Path.GetFileName(targetPath);
            storedPath = targetPath;

            try
            {
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
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory, cancellationToken);
                throw;
            }

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

    public async Task<string> ExportItemToDirectoryAsync(
        Guid itemId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
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
        var targetPath = FileNameService.GetUniqueDestinationPath(fullTargetDirectory, displayName, isDirectory);
        PathSafety.EnsureChildPath(fullTargetDirectory, targetPath);

        await SafeFileOps.MoveAsync(sourcePath, targetPath, isDirectory, cancellationToken);

        try
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }
        catch
        {
            await TryCompensateMoveAsync(targetPath, sourcePath, isDirectory, cancellationToken);
            throw;
        }

        return targetPath;
    }

    public async Task<ItemDeleteResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
            return ItemDeleteResult.ReferenceRemoved(item.Id, item.DisplayName);
        }

        var restore = await RestoreStoredItemAsync(item, reservedTargets: null, cancellationToken);
        try
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }
        catch
        {
            // Best effort: try to put the file back into box storage if the DB write failed.
            if (!string.IsNullOrWhiteSpace(item.StoredPath) && !string.IsNullOrWhiteSpace(restore.RestoredPath))
            {
                var isDirectory = item.ItemKind == ItemKind.Directory;
                await TryCompensateMoveAsync(restore.RestoredPath, item.StoredPath, isDirectory, cancellationToken);
            }

            throw;
        }

        return restore;
    }

    public async Task<BoxDeleteResult> DeleteBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
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
                await RestoreStoredItemAsync(item, reservedTargets, cancellationToken);
                await _repository.RemoveItemAsync(item.Id, cancellationToken);
                restoredCount++;
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

    private async Task<ItemDeleteResult> RestoreStoredItemAsync(
        DrawerItem item,
        HashSet<string>? reservedTargets,
        CancellationToken cancellationToken)
    {
        var plan = CreateRestorePlan(item, reservedTargets);
        await SafeFileOps.MoveAsync(plan.SourcePath, plan.TargetPath, plan.IsDirectory, cancellationToken);

        return new ItemDeleteResult(
            item.Id,
            item.DisplayName,
            WasStoredItem: true,
            RestoredPath: plan.TargetPath,
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

    /// <summary>
    /// Wraps a file-move permission failure with an actionable hint. The most
    /// common cause is dragging from a read-only system location such as the
    /// public desktop (C:\Users\Public\Desktop), where the source directory is
    /// not writable and the file cannot be deleted as part of the move.
    /// </summary>
    internal static InvalidOperationException CreateImportPermissionException(
        string displayName,
        string sourcePath,
        UnauthorizedAccessException innerException)
    {
        var commonDesktop = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDesktopDirectory);
        var isPublicDesktop = !string.IsNullOrWhiteSpace(commonDesktop)
            && sourcePath.StartsWith(commonDesktop, StringComparison.OrdinalIgnoreCase);
        var hint = isPublicDesktop
            ? "源位置（公共桌面）默认只读，无法从中移走文件。"
            : "源目录没有写入权限，无法移走文件。";
        return new InvalidOperationException(
            $"无法收纳“{displayName}”：{hint}"
            + "可改用“映射收纳盒”（仅保存引用、不移动文件），"
            + "或先将文件复制到可写位置再拖入。",
            innerException);
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
            if ((isDirectory ? Directory.Exists(candidate) : File.Exists(candidate))
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

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        return _repository.SetSettingAsync(key, value, cancellationToken);
    }

    public async Task RenameBoxAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(newName));
        }

        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        await _repository.UpdateBoxNameAsync(boxId, newName.Trim(), cancellationToken);
    }

    /// <summary>
    /// Renames the drawer item's display name only. The underlying file on disk
    /// keeps its real name, so this never mutates the user's file system entry.
    /// A trailing ".lnk" is re-appended when the stored name used it, keeping the
    /// shortcut look consistent for mapping boxes.
    /// </summary>
    public async Task RenameItemAsync(
        Guid itemId,
        string newDisplayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = newDisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Item name cannot be empty.", nameof(newDisplayName));
        }

        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        var storedName = item.DisplayName;
        var storeWithLinkSuffix = storedName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        if (storeWithLinkSuffix
            && !normalizedName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName += ".lnk";
        }

        await _repository.UpdateItemDisplayNameAsync(itemId, normalizedName, cancellationToken);
    }

    public async Task OpenItemAsAdminAsync(
        Guid itemId,
        IFileLauncher launcher,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (item.ItemKind == ItemKind.Directory)
        {
            throw new InvalidOperationException("Folders cannot be opened with administrator rights.");
        }

        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.OpenAsAdminAsync(path, cancellationToken);
    }

    public async Task ShowItemInFolderAsync(
        Guid itemId,
        IFileLauncher launcher,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.ShowInFolderAsync(path, cancellationToken);
    }

    /// <summary>
    /// Merges every item of <paramref name="sourceBoxId"/> into
    /// <paramref name="targetBoxId"/> and then deletes the source box.
    /// Stored items are physically moved into the target box storage;
    /// mapping references are re-pointed at the target box.
    /// </summary>
    /// <returns>The number of items merged.</returns>
    public async Task<int> MergeBoxAsync(
        Guid sourceBoxId,
        Guid targetBoxId,
        CancellationToken cancellationToken = default)
    {
        if (sourceBoxId == targetBoxId)
        {
            throw new InvalidOperationException("Cannot merge a box into itself.");
        }

        var sourceBox = await _repository.GetBoxAsync(sourceBoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        var targetBox = await _repository.GetBoxAsync(targetBoxId, cancellationToken)
            ?? throw new InvalidOperationException("Target box does not exist.");

        if (sourceBox.Type == BoxType.Todo || targetBox.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Todo boxes cannot be merged.");
        }

        var sourceIsMapping = sourceBox.Type == BoxType.Mapping;
        var targetIsMapping = targetBox.Type == BoxType.Mapping;
        if (sourceIsMapping != targetIsMapping)
        {
            throw new InvalidOperationException(
                "Mapping boxes can only be merged into other mapping boxes, "
                + "and storage boxes only into other storage boxes.");
        }

        var items = await _repository.GetItemsAsync(sourceBoxId, cancellationToken);

        // 预检：映射盒的每一项都必须是无存储路径的引用，存储盒的每一项都必须
        // 有可移动的存储文件，避免中途失败后才暴露数据异常。
        foreach (var item in items)
        {
            if (sourceIsMapping && !string.IsNullOrWhiteSpace(item.StoredPath))
            {
                throw new InvalidOperationException(
                    $"“{item.DisplayName}”是存储项，不能并入映射盒。");
            }

            if (!sourceIsMapping && string.IsNullOrWhiteSpace(item.StoredPath))
            {
                throw new InvalidOperationException(
                    $"“{item.DisplayName}”没有可移动的存储文件。");
            }
        }

        var movedItems = new List<DrawerItem>(items.Count);
        try
        {
            foreach (var item in items)
            {
                await MoveItemToBoxAsync(item.Id, targetBoxId, cancellationToken: cancellationToken);
                movedItems.Add(item);
            }
        }
        catch (Exception exception)
        {
            // Best-effort rollback: move already-merged items back to the source
            // box so a failed merge does not leave a half-migrated state.
            foreach (var item in movedItems)
            {
                try
                {
                    await MoveItemToBoxAsync(item.Id, sourceBoxId, cancellationToken: cancellationToken);
                }
                catch
                {
                    // Rollback is best-effort; the original failure is rethrown.
                }
            }

            throw new InvalidOperationException(
                $"合并中断（已移动 {movedItems.Count}/{items.Count} 项，尝试回滚）：{exception.Message}",
                exception);
        }

        var deleteResult = await DeleteBoxAsync(sourceBoxId, cancellationToken);
        if (!deleteResult.BoxRemoved)
        {
            throw new InvalidOperationException(
                "Items were merged but the source box could not be removed: "
                + string.Join("; ", deleteResult.Failures));
        }

        return items.Count;
    }

    /// <summary>
    /// One-click classification: moves every mapping reference (items that point
    /// at external files) into per-type mapping boxes ("图片收纳盒", "文档收纳盒",
    /// ...). Stored items are intentionally left untouched because they must not
    /// be moved into mapping boxes.
    /// </summary>
    public async Task<ClassifyResult> ClassifyMappingItemsAsync(
        CancellationToken cancellationToken = default)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        var allItems = await _repository.GetItemsAsync(null, cancellationToken);

        var targetBoxes = new Dictionary<string, Box>(StringComparer.OrdinalIgnoreCase);
        var movedCount = 0;
        var skippedCount = 0;

        foreach (var item in allItems)
        {
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                skippedCount++;
                continue;
            }

            var category = ClassifyCategory.GetCategory(
                item.DisplayName,
                item.SourcePath,
                isDirectory: item.ItemKind == ItemKind.Directory);
            if (!targetBoxes.TryGetValue(category.BoxName, out var targetBox))
            {
                targetBox = boxes.FirstOrDefault(box => box.Type == BoxType.Mapping
                    && string.Equals(box.Name, category.BoxName, StringComparison.OrdinalIgnoreCase))
                    ?? await CreateBoxAsync(category.BoxName, BoxType.Mapping, cancellationToken);
                targetBoxes[category.BoxName] = targetBox;
            }

            if (item.BoxId == targetBox.Id)
            {
                skippedCount++;
                continue;
            }

            await MoveItemToBoxAsync(item.Id, targetBox.Id, cancellationToken: cancellationToken);
            movedCount++;
        }

        return new ClassifyResult(movedCount, skippedCount);
    }

    public async Task OpenItemAsync(Guid itemId, IFileLauncher launcher, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
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
        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var missingItemIds = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items
                .Where(IsMissingStoredItem)
                .Select(item => item.Id)
                .ToArray();
        }, cancellationToken);

        foreach (var itemId in missingItemIds)
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }
    }

    private static bool IsMissingStoredItem(DrawerItem item)
    {
        return !string.IsNullOrWhiteSpace(item.StoredPath)
            && !File.Exists(item.StoredPath)
            && !Directory.Exists(item.StoredPath);
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

    private static async Task TryCompensateMoveAsync(
        string movedPath,
        string originalPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            if ((isDirectory && Directory.Exists(movedPath)) || (!isDirectory && File.Exists(movedPath)))
            {
                await SafeFileOps.MoveAsync(movedPath, originalPath, isDirectory, cancellationToken);
            }
        }
        catch
        {
            // Best-effort compensation only; the original failure is rethrown by the caller.
        }
    }

    private sealed record RestorePlan(
        string SourcePath,
        string TargetPath,
        bool IsDirectory,
        bool RestoredToOriginal,
        bool RestoredToDesktop);
}
