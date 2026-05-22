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

    public async Task<DrawerItem> ImportPathAsync(Guid boxId, string sourcePath, CancellationToken cancellationToken = default)
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
                now);
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
                now);
        }

        await _repository.AddItemAsync(item, cancellationToken);
        return item;
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
            var storagePath = box.StoragePath ?? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, storagePath);

            if (Directory.Exists(storagePath))
            {
                await trash.MoveToRecycleBinAsync(storagePath, cancellationToken);
            }
        }

        await _repository.RemoveBoxAsync(boxId, cancellationToken);
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
}
