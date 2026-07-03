using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class DrawerService
{
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly IAppLogger? _logger;
    private readonly MissingItemTracker _missingItemTracker;

    public DrawerService(AppPaths paths, DrawerRepository repository)
        : this(paths, repository, logger: null, missingItemTracker: new MissingItemTracker())
    {
    }

    public DrawerService(AppPaths paths, DrawerRepository repository, IAppLogger? logger)
        : this(paths, repository, logger, new MissingItemTracker())
    {
    }

    private DrawerService(AppPaths paths, DrawerRepository repository, IAppLogger? logger, MissingItemTracker missingItemTracker)
    {
        _paths = paths;
        _repository = repository;
        _logger = logger;
        _missingItemTracker = missingItemTracker;
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
        var storagePath = (type == BoxType.Normal || type == BoxType.Pixel) ? Path.Combine(_paths.BoxesDirectory, id.ToString("N")) : null;
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

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isDirectory)
                {
                    Directory.Move(fullSourcePath, targetPath);
                }
                else
                {
                    File.Move(fullSourcePath, targetPath);
                }
            }, cancellationToken);

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
        }

        // Move-then-persist is inherently non-atomic. If the DB write fails after the file has
        // already been relocated, roll the file back to its source so we never leave an orphan
        // the user cannot see or recover.
        try
        {
            await _repository.AddItemAsync(item, cancellationToken);
        }
        catch (Exception persistException) when (item.StoredPath is not null)
        {
            _logger?.Error(persistException, $"Persisting imported item failed; rolling file back to source.");
            await TryRollbackMoveAsync(item.StoredPath, fullSourcePath, isDirectory, cancellationToken);
            throw;
        }
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

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isDirectory)
                {
                    Directory.Move(fullSourcePath, targetPath);
                }
                else
                {
                    File.Move(fullSourcePath, targetPath);
                }
            }, cancellationToken);

            displayName = Path.GetFileName(targetPath);
            storedPath = targetPath;

            // Persist with rollback: if the DB update fails, move the file back to its previous
            // location so the item is neither lost nor stranded in the new box without a record.
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
            catch (Exception persistException)
            {
                _logger?.Error(persistException, "Persisting cross-box move failed; rolling file back to source.");
                await TryRollbackMoveAsync(targetPath, fullSourcePath, isDirectory, cancellationToken);
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

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isDirectory)
            {
                Directory.Move(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
            }
        }, cancellationToken);

        // If the record cannot be removed after the export move, put the file back so the box
        // stays consistent and the item is still reachable.
        try
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }
        catch (Exception persistException)
        {
            _logger?.Error(persistException, "Removing exported item record failed; rolling file back into box storage.");
            await TryRollbackMoveAsync(targetPath, sourcePath, isDirectory, cancellationToken);
            throw;
        }
        return targetPath;
    }

    public async Task DeleteItemAsync(Guid itemId, IFileTrash trash, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (item.StoredPath is not null && (File.Exists(item.StoredPath) || Directory.Exists(item.StoredPath)))
        {
            await trash.MoveToRecycleBinAsync(item.StoredPath, cancellationToken);
        }

        await _repository.RemoveItemAsync(itemId, cancellationToken);
    }

    public async Task DeleteBoxAsync(Guid boxId, IFileTrash trash, CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        if (box.Type == BoxType.Normal || box.Type == BoxType.Pixel)
        {
            await RestoreItemsToDesktopAsync(boxId, cancellationToken);
        }

        await _repository.RemoveBoxAsync(boxId, cancellationToken);
    }

    private async Task RestoreItemsToDesktopAsync(Guid boxId, CancellationToken cancellationToken)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            return;
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StoredPath))
            {
                continue;
            }

            var sourcePath = item.StoredPath;
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                continue;
            }

            // Restore each file independently. A single failure (desktop full, name clash,
            // permission) must not abort the whole box deletion and leave the remaining files
            // stranded inside storage that is about to be unreferenced.
            try
            {
                var fileName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var isDirectory = Directory.Exists(sourcePath);
                var targetPath = FileNameService.GetUniqueDestinationPath(desktopPath, fileName, isDirectory);

                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (isDirectory)
                    {
                        Directory.Move(sourcePath, targetPath);
                    }
                    else
                    {
                        File.Move(sourcePath, targetPath);
                    }
                }, cancellationToken);
            }
            catch (Exception restoreException)
            {
                _logger?.Error(restoreException, $"Failed to restore item {item.Id} to desktop during box deletion; skipping.");
            }
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
        if (newName == null)
            newName = string.Empty;

        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        await _repository.UpdateBoxNameAsync(boxId, newName, cancellationToken);
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

    // Reads no longer delete on first miss. Each stored item whose backing file is absent is
    // tallied by the tracker; only after MissingThreshold consecutive misses is the record
    // pruned, with a log line explaining the decision. Transient misses (locked file, detached
    // network drive, AV quarantine) therefore do not silently erase the user's data.
    private async Task PruneMissingStoredItemsAsync(Guid? boxId, CancellationToken cancellationToken)
    {
        var items = await _repository.GetItemsAsync(boxId, cancellationToken);

        var itemsToPrune = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toPrune = new List<DrawerItem>();
            foreach (var item in items)
            {
                var missing = !string.IsNullOrWhiteSpace(item.StoredPath)
                    && !File.Exists(item.StoredPath)
                    && !Directory.Exists(item.StoredPath);

                if (_missingItemTracker.Record(item, missing) == MissingItemTracker.RecordOutcome.Prune)
                {
                    toPrune.Add(item);
                }
            }
            return toPrune;
        }, cancellationToken);

        foreach (var item in itemsToPrune)
        {
            _logger?.Info($"Pruned missing item {item.Id} ('{item.DisplayName}') after {MissingItemTracker.MissingThreshold} consecutive missing reads.");
            await _repository.RemoveItemAsync(item.Id, cancellationToken);
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

    // Best-effort rollback used when a persist step fails after a file move. We never let a
    // rollback error mask the original failure: it is logged and swallowed so the caller sees
    // the real cause. If the destination is already gone (concurrent delete) we silently move on.
    private async Task TryRollbackMoveAsync(string currentPath, string originalPath, bool isDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if (isDirectory ? !Directory.Exists(currentPath) : !File.Exists(currentPath))
            {
                return;
            }

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isDirectory)
                {
                    Directory.Move(currentPath, originalPath);
                }
                else
                {
                    File.Move(currentPath, originalPath);
                }
            }, cancellationToken);
        }
        catch (Exception rollbackException)
        {
            _logger?.Error(rollbackException, $"Rollback move from '{currentPath}' to '{originalPath}' failed.");
        }
    }
}
