using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

public sealed partial class DrawerService
{
    public Task<DrawerItem> GetItemInBoxAsync(Guid boxId, Guid itemId, CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            var item = await _repository.GetItemAsync(itemId, cancellationToken)
                ?? throw new InvalidOperationException("文件已移除，请刷新后重试。");
            if (item.BoxId != boxId)
                throw new InvalidOperationException("文件已移动到其他盒子，请刷新后重试。");
            return item;
        }, cancellationToken);

    public Task<DrawerItem> CopyPathToBoxAsync(Guid boxId, string sourcePath, CancellationToken cancellationToken = default)
        => Task.Run(() => CopyPathToBoxCoreAsync(boxId, sourcePath, cancellationToken), cancellationToken);

    private async Task<DrawerItem> CopyPathToBoxCoreAsync(Guid boxId, string sourcePath, CancellationToken cancellationToken)
    {
        await _fileOperationGate.WaitAsync(cancellationToken);
        try
        {
            var box = await _repository.GetBoxAsync(boxId, cancellationToken)
                ?? throw new InvalidOperationException("盒子已删除。");
            if (box.Type is BoxType.Mapping or BoxType.Todo)
                throw new InvalidOperationException("只能复制文件到普通收纳盒。");
            var fullSource = PathSafety.GetFullExistingPath(sourcePath);
            var originalPath = fullSource;
            var isDirectory = Directory.Exists(fullSource);
            if (IsSameOrDescendant(fullSource, _paths.BoxesDirectory))
            {
                PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullSource);
                var items = await _repository.GetItemsAsync(null, cancellationToken);
                var sourceItem = items.FirstOrDefault(item => item.StoredPath is not null
                    && (string.Equals(item.StoredPath, fullSource, StringComparison.OrdinalIgnoreCase)
                        || (item.ItemKind == ItemKind.Directory && IsSameOrDescendant(fullSource, item.StoredPath))))
                    ?? throw new InvalidOperationException("不能复制收纳盒目录或内部恢复文件。");
                if (string.IsNullOrWhiteSpace(sourceItem.SourcePath))
                    throw new InvalidOperationException("来源位置不可用，无法安全记录复制文件的还原位置。");
                originalPath = string.Equals(sourceItem.StoredPath, fullSource, StringComparison.OrdinalIgnoreCase)
                    ? sourceItem.SourcePath
                    : Path.Combine(sourceItem.SourcePath, Path.GetRelativePath(sourceItem.StoredPath!, fullSource));
            }
            else
            {
                ValidateImportSource(fullSource);
            }
            var pending = await _repository.GetPendingFileOperationsAsync(cancellationToken);
            if (pending.Any(operation => IsSameOrDescendant(fullSource, operation.SourcePath)
                || IsSameOrDescendant(operation.SourcePath, fullSource)
                || IsSameOrDescendant(fullSource, operation.TargetPath)
                || IsSameOrDescendant(operation.TargetPath, fullSource)))
                throw new InvalidOperationException("源文件有待恢复的操作，请完成恢复后再复制。");

            var storage = box.StoragePath ?? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, storage);
            Directory.CreateDirectory(storage);
            var name = Path.GetFileName(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var target = FileNameService.GetUniqueDestinationPath(storage, name, isDirectory);
            PathSafety.EnsureChildPath(storage, target);
            var now = DateTimeOffset.UtcNow;
            var item = new DrawerItem(Guid.NewGuid(), boxId, Path.GetFileName(target),
                isDirectory ? ItemKind.Directory : ItemKind.File, originalPath, target,
                await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken), now, now);
            var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Copy,
                item.Id, fullSource, target, isDirectory, item);
            await _repository.AddPendingFileOperationAsync(operation, cancellationToken);
            try
            {
                await SafeFileOps.CopyAsync(fullSource, target, isDirectory, cancellationToken, operation.Id);
            }
            catch
            {
                var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
                if (!PathExists(stage)) await ClearPendingOperationAsync(operation.Id);
                else _retryPendingRecoveryOnRead = true;
                throw;
            }
            await CompleteJournaledOperationAsync(operation);
            Changes.Publish(boxId);
            return item;
        }
        finally { _fileOperationGate.Release(); }
    }

    public Task<DrawerItem> RenameItemAsync(Guid boxId, Guid itemId, string newName, CancellationToken cancellationToken = default)
        => Task.Run(() => RenameItemCoreAsync(boxId, itemId, newName, cancellationToken), cancellationToken);

    public static void ValidateItemName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("名称不能为空，也不能包含非法字符或以空格、句点结尾。");
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
            throw new ArgumentException("不能使用 Windows 保留名称。");
    }

    private async Task<DrawerItem> RenameItemCoreAsync(Guid boxId, Guid itemId, string newName, CancellationToken cancellationToken)
    {
        ValidateItemName(newName);
        await _fileOperationGate.WaitAsync(cancellationToken);
        try
        {
            var item = await GetItemInBoxAsync(boxId, itemId, cancellationToken);
            if (item.DisplayName == newName) return item;
            var pending = await _repository.GetPendingFileOperationsAsync(cancellationToken);
            if (pending.Any(operation => operation.ItemId == itemId))
                throw new InvalidOperationException("文件有待恢复的操作，请完成恢复后再重命名。");
            if (item.StoredPath is null)
            {
                await _repository.MoveItemToBoxAsync(item, boxId, newName, item.SourcePath, null,
                    item.SortOrder, item.GridColumn, item.GridRow, cancellationToken);
                Changes.Publish(boxId);
                return item with { DisplayName = newName };
            }
            var source = PathSafety.GetFullExistingPath(item.StoredPath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, source);
            var parent = Path.GetDirectoryName(source)!;
            var target = Path.Combine(parent, newName);
            var caseOnly = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly) target = FileNameService.GetUniqueDestinationPath(parent, newName, item.ItemKind == ItemKind.Directory);
            PathSafety.EnsureChildPath(parent, target);
            var originalParent = string.IsNullOrWhiteSpace(item.SourcePath) ? null : Path.GetDirectoryName(item.SourcePath);
            var resultItem = item with
            {
                DisplayName = Path.GetFileName(target), StoredPath = target,
                SourcePath = originalParent is null ? null : Path.Combine(originalParent, Path.GetFileName(target)),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Rename,
                item.Id, source, target, item.ItemKind == ItemKind.Directory, resultItem);
            await _repository.AddPendingFileOperationAsync(operation, cancellationToken);
            try
            {
                if (caseOnly)
                {
                    var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
                    await SafeFileOps.MoveAsync(source, stage, operation.IsDirectory, cancellationToken);
                    await SafeFileOps.MoveAsync(stage, target, operation.IsDirectory, CancellationToken.None);
                }
                else await SafeFileOps.MoveAsync(source, target, operation.IsDirectory, cancellationToken, operation.Id);
            }
            catch
            {
                _retryPendingRecoveryOnRead = true;
                await TryClearAbortedOperationAsync(operation);
                throw;
            }
            await CompleteJournaledOperationAsync(operation);
            Changes.Publish(boxId);
            return resultItem;
        }
        finally { _fileOperationGate.Release(); }
    }

    private async Task RecoverCopyAsync(PendingFileOperation operation, CancellationToken cancellationToken)
    {
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, operation.TargetPath);
        var stage = SafeFileOps.CreateStagingPath(operation.TargetPath, operation.Id);
        if (PathExists(stage) && PathExists(operation.TargetPath))
            throw new IOException("复制暂存文件与目标同时存在，请保留两者并核对。");
        if (await _repository.GetItemAsync(operation.ItemId, cancellationToken) is { } current)
        {
            if (!string.Equals(current.StoredPath, operation.TargetPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("复制记录与目标位置不一致，请保留文件并核对。");
            await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
            return;
        }
        if (PathExists(operation.TargetPath))
        {
            await _repository.CompletePendingFileOperationAsync(operation, CancellationToken.None);
            return;
        }
        // A staging copy was never promoted and may be incomplete. Preserve it if
        // the original has also disappeared; otherwise the untouched source wins.
        if (!PathExists(operation.SourcePath)) throw new IOException("复制源不可用，请保留复制暂存文件并核对。");
        SafeFileOps.DeleteRecoveryArtifact(stage, operation.IsDirectory);
        await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
    }

    private async Task RecoverRenameAsync(PendingFileOperation operation, CancellationToken cancellationToken)
    {
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, operation.SourcePath);
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, operation.TargetPath);
        if (operation.IsCompensating)
        {
            await RecoverCompensationAsync(operation, cancellationToken);
            return;
        }
        var stage = SafeFileOps.CreateStagingPath(operation.TargetPath, operation.Id);
        var caseOnly = string.Equals(operation.SourcePath, operation.TargetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly)
        {
            await RecoverPendingFileOperationAsync(operation with { Kind = PendingFileOperationKind.Move }, cancellationToken);
            return;
        }
        if (PathExists(stage))
        {
            if (PathExists(operation.TargetPath)) throw new IOException("重命名源位置已被重新占用，请保留暂存文件并核对。");
            await SafeFileOps.MoveAsync(stage, operation.TargetPath, operation.IsDirectory, cancellationToken);
            await _repository.CompletePendingFileOperationAsync(operation, CancellationToken.None);
            return;
        }
        var current = await _repository.GetItemAsync(operation.ItemId, cancellationToken);
        if (current?.StoredPath == operation.TargetPath && current.DisplayName == operation.ResultItem?.DisplayName)
        {
            await _repository.RemovePendingFileOperationAsync(operation.Id, cancellationToken);
            return;
        }
        var targetRenamed = PathExists(operation.TargetPath) && Directory.EnumerateFileSystemEntries(
            Path.GetDirectoryName(operation.TargetPath)!).Any(path => Path.GetFileName(path) == Path.GetFileName(operation.TargetPath));
        if (targetRenamed)
        {
            await _repository.CompletePendingFileOperationAsync(operation, CancellationToken.None);
            return;
        }
        // Reuse the move recovery checks (including held sources and partial copies)
        // only when the forward rename has not reached its final name yet.
        await RecoverPendingFileOperationAsync(operation with { Kind = PendingFileOperationKind.Move }, cancellationToken);
    }
}
