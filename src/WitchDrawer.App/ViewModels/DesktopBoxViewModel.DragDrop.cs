using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的拖放部分：文件导入/移动/导出、拖拽预览与画布尺寸。</summary>
public sealed partial class DesktopBoxViewModel
{
    public (int Column, int Row) GetGridSlot(
        double x,
        double y,
        double surfaceWidth = 0,
        double surfaceHeight = 0)
    {
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));

        // Edge expansion: when the pointer reaches the right/bottom edge of the *content*
        // grid, target a brand-new column/row so the box grows by one cell. 固定模式下
        // 边缘扩展仍然生效（窗口随内容生长），但最终格位会被钳制在 m×n 上限内。
        // The reference is the item-grid extent ((maxCol+1)*slotWidth), which stays constant
        // while dragging. Using the live window/IconList size here would create a feedback
        // loop: expanding grows the window, which moves the edge away from the pointer, which
        // un-expands, which shrinks the window... — the box would flicker at the threshold.

        if (surfaceWidth > 0 && surfaceHeight > 0)
        {
            // 画布刚因预览扩展而改尺寸后的极短窗口内，指针坐标读取处于布局过渡态，
            // 会读出瞬时错位值：此时直接保持当前预览格，等布局稳定后再跟随指针。
            // 否则扩展帧与错位帧交替 → 扩展/收缩来回打摆（空盒上表现为疯狂频闪）。
            // 50ms ≈ 60Hz 下 3 帧 / 120Hz 下 6 帧，足够覆盖过渡态又不会影响跟随手感。
            if (IsDragPreviewVisible
                && (DateTime.UtcNow - _lastCanvasSizeChangedUtc).TotalMilliseconds < 50)
            {
                return (_previewColumn, _previewRow);
            }

            var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
            var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

            var contentRight = (maxCol + 1) * LayoutSettings.ItemSlotWidth;
            var contentBottom = (maxRow + 1) * LayoutSettings.ItemSlotHeight;

            if (x >= contentRight - EdgeExpandThreshold)
            {
                column = Math.Max(column, maxCol + 1);
            }

            if (y >= contentBottom - EdgeExpandThreshold)
            {
                row = Math.Max(row, maxRow + 1);
            }
        }

        if (IsFixedSize)
        {
            column = Math.Min(column, _sizeMode.Columns - 1);
            row = Math.Min(row, _sizeMode.Rows - 1);
        }

        return (column, row);
    }

    public void UpdateDragPreview(double x, double y)
    {
        IsDragPreviewVisible = true;
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));
        if (IsDragPreviewVisible && _previewColumn == column && _previewRow == row
            && _dragPreviewWidthOverride is null && _dragPreviewHeightOverride is null)
        {
            return;
        }

        _previewColumn = column;
        _previewRow = row;
    }

    public void ShowDragPreview(int column, int row)
    {
        if (!ShouldShowGridDragPreview(IsMappingListMode, IsDrawerCollapsed))
        {
            // A collapsed drawer shows the cover tiles, not the item grid, so a positional
            // frame cannot line up with what the user sees. Growing the preview canvas
            // here would also resize the SizeToContent window under the stationary
            // cursor, feeding back into the slot calculation and oscillating.
            IsDragPreviewVisible = false;
            return;
        }

        // OLE repeats DragOver even while the pointer stays in the same slot.
        // Do not scan every item or invalidate bindings again for that case.
        if (IsDragPreviewVisible && _previewColumn == column && _previewRow == row
            && _dragPreviewWidthOverride is null && _dragPreviewHeightOverride is null)
        {
            return;
        }

        _previewColumn = column;
        _previewRow = row;
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
        IsDragPreviewVisible = true;
        UpdateGridCanvasSize(positionsChanged: false);

        DragPreviewLeft = (column * LayoutSettings.ItemSlotWidth) + LayoutSettings.ItemSpacing;
        DragPreviewTop = (row * LayoutSettings.ItemSlotHeight) + LayoutSettings.ItemSpacing;
    }

    public void HideDragPreview()
    {
        if (!IsDragPreviewVisible && _dragPreviewWidthOverride is null && _dragPreviewHeightOverride is null)
        {
            return;
        }

        IsDragPreviewVisible = false;
        _previewColumn = 0;
        _previewRow = 0;
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
        UpdateGridCanvasSize(positionsChanged: false);
    }

    // Free-form preview used by the collapsed drawer cover: the frame is placed
    // directly over the cover cell the dropped item will occupy, in the preview
    // canvas' coordinate space, instead of using item-grid slot math.
    public void ShowDragPreviewAt(double left, double top, double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (IsDragPreviewVisible && DragPreviewLeft == left && DragPreviewTop == top
            && _dragPreviewWidthOverride == width && _dragPreviewHeightOverride == height)
        {
            return;
        }

        _previewColumn = 0;
        _previewRow = 0;
        _dragPreviewWidthOverride = Math.Max(1, width);
        _dragPreviewHeightOverride = Math.Max(1, height);
        DragPreviewLeft = left;
        DragPreviewTop = top;
        IsDragPreviewVisible = true;
        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    public (int Column, int Row) GetAvailableDropSlot(int targetColumn, int targetRow, Guid? movingItemId = null)
    {
        var targetSlot = NormalizeGridSlot(targetColumn, targetRow);
        var occupiedSlots = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => (item.GridColumn, item.GridRow))
            .ToHashSet();

        return FindFirstFreeSlot(targetSlot.Column, targetSlot.Row, occupiedSlots);
    }

    /// <summary>
    /// 固定模式下是否还有空位可以放入（拖入校验用；自适应模式恒为 true）。
    /// </summary>
    public bool HasFreeSlotForDrop(Guid? movingItemId = null)
    {
        if (!IsFixedSize)
        {
            return true;
        }

        var occupied = Items.Count(item => movingItemId is null || item.Id != movingItemId.Value);
        return occupied < FixedCapacity;
    }

    /// <summary>
    /// 自适应模式等价于 <see cref="GetAvailableDropSlot"/>；固定模式把目标格钳制在
    /// m×n 边界内，找不到空位时返回 false（硬约束：放不下就是放不下）。
    /// </summary>
    public bool TryGetAvailableDropSlot(
        int targetColumn,
        int targetRow,
        Guid? movingItemId,
        out (int Column, int Row) slot)
    {
        if (!IsFixedSize)
        {
            slot = GetAvailableDropSlot(targetColumn, targetRow, movingItemId);
            return true;
        }

        var occupiedSlots = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => (item.GridColumn, item.GridRow))
            .ToHashSet();

        return TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out slot);
    }

    public Task ImportPathsAsync(IEnumerable<string> paths)
    {
        return ImportPathsAsync(paths, null, null);
    }

    public async Task<IReadOnlyList<Guid>> ImportPathsAsync(IEnumerable<string> paths, int? startColumn, int? startRow)
    {
        var pathList = paths.ToArray();
        if (pathList.Length == 0 || IsBusy)
        {
            return Array.Empty<Guid>();
        }

        var importedIds = new List<Guid>(pathList.Length);
        try
        {
            IsBusy = true;
            var reservedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
            var nextColumn = startColumn ?? 0;
            var nextRow = startRow ?? 0;
            foreach (var path in pathList)
            {
                if (!IsFreeSort)
                {
                    // 排序模式：不写格位（显示位置由排序键决定），自由布局不受污染；
                    // 固定盒容量硬约束仍然生效：装满即停止导入。
                    if (IsFixedSize && Items.Count + importedIds.Count >= FixedCapacity)
                    {
                        break;
                    }

                    var sortedImport = await _drawerService.ImportPathAsync(BoxId, path);
                    importedIds.Add(sortedImport.Id);
                    await _shellChangeNotifier.NotifyItemImportedAsync(sortedImport, _logger);
                    continue;
                }

                (int Column, int Row) slot;
                if (IsFixedSize)
                {
                    // 硬约束：固定模式装满即停止导入，剩余文件保持原样。
                    if (!TryFindFreeSlotInFixedBounds(nextColumn, nextRow, reservedSlots, out slot))
                    {
                        break;
                    }
                }
                else
                {
                    slot = FindFirstFreeSlot(nextColumn, nextRow, reservedSlots);
                }

                reservedSlots.Add(slot);
                var importedItem = await _drawerService.ImportPathAsync(BoxId, path, slot.Column, slot.Row);
                importedIds.Add(importedItem.Id);
                await _shellChangeNotifier.NotifyItemImportedAsync(importedItem, _logger);
                nextColumn = slot.Column + 1;
                nextRow = slot.Row;
            }

            await LoadAsync();
            StatusText = importedIds.Count < pathList.Length
                ? importedIds.Count > 0
                    ? $"已收纳 {importedIds.Count} 项，盒子已满"
                    : "盒子已满，无法收纳"
                : $"已收纳 {importedIds.Count} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return importedIds;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import into desktop box.");
            if (importedIds.Count > 0)
            {
                try
                {
                    await LoadAsync();
                }
                catch (Exception refreshException)
                {
                    _logger.Error(refreshException, "Failed to refresh partially imported files.");
                }
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }
            StatusText = $"已收纳 {importedIds.Count} 项，其余未导入：{exception.Message}";
            return importedIds;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DropDrawerItemAsync(Guid itemId, int targetColumn, int targetRow)
    {
        if (IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            var movedAcrossBoxes = false;
            var currentItem = Items.FirstOrDefault(item => item.Id == itemId);
            if (currentItem is not null)
            {
                // 排序模式：盒内拖动不换位（显示顺序由排序键决定），落放为空操作。
                if (IsFreeSort)
                {
                    if (IsMappingListMode)
                    {
                        await MoveMappingListItemWithinBoxAsync(currentItem, targetRow);
                    }
                    else
                    {
                        await MoveItemWithinBoxAsync(currentItem, targetColumn, targetRow);
                    }
                }
            }
            else
            {
                if (IsFreeSort)
                {
                    var occupiedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
                    (int Column, int Row) targetSlot;
                    if (IsFixedSize)
                    {
                        // 硬约束：目标盒已满时拒绝跨盒移入。
                        if (!TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out targetSlot))
                        {
                            StatusText = "目标收纳盒已满";
                            return false;
                        }
                    }
                    else
                    {
                        targetSlot = FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
                    }
                    await _drawerService.MoveItemToBoxAsync(itemId, BoxId, targetSlot.Column, targetSlot.Row);
                }
                else
                {
                    // 排序模式：固定盒容量校验后直接移入，不写格位。
                    if (IsFixedSize && !HasFreeSlotForDrop())
                    {
                        StatusText = "目标收纳盒已满";
                        return false;
                    }

                    await _drawerService.MoveItemToBoxAsync(itemId, BoxId);
                }

                await LoadAsync();
                movedAcrossBoxes = true;
            }

            if (movedAcrossBoxes)
            {
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to move desktop box item.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CompleteDragOutAsync(DrawerItemViewModel? item)
    {
        return DeleteItemAsync(item);
    }

    public async Task<bool> ExportItemToDesktopAsync(DrawerItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopDirectory))
            {
                desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            var exportedPath = await _drawerService.ExportItemToDirectoryAsync(item.Id, desktopDirectory);
            await Task.Run(() => _shellChangeNotifier.NotifyFolderItemCreated(
                exportedPath,
                item.Model.ItemKind == ItemKind.Directory));
            await LoadAsync();
            StatusText = $"已移到桌面：{Path.GetFileName(exportedPath)}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to export desktop box item.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MoveItemWithinBoxAsync(DrawerItemViewModel item, int targetColumn, int targetRow)
    {
        var targetSlot = NormalizeGridSlot(targetColumn, targetRow);
        targetColumn = targetSlot.Column;
        targetRow = targetSlot.Row;

        if (item.GridColumn == targetColumn && item.GridRow == targetRow)
        {
            return;
        }

        var occupiedSlots = Items
            .Where(candidate => candidate.Id != item.Id)
            .Select(candidate => (candidate.GridColumn, candidate.GridRow))
            .ToHashSet();
        var availableSlot = IsFixedSize
            ? TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out var fixedSlot)
                ? fixedSlot
                : (Column: item.GridColumn, Row: item.GridRow)
            : FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
        targetColumn = availableSlot.Column;
        targetRow = availableSlot.Row;

        await _drawerService.UpdateItemGridPositionAsync(item.Id, targetColumn, targetRow);
        item.SetGridPosition(targetColumn, targetRow, LayoutSettings);
        UpdateGridCanvasSize();
    }

    private void UpdateGridCanvasSize(bool positionsChanged = true)
    {
        if (positionsChanged)
        {
            GridLayoutVersion = unchecked(GridLayoutVersion + 1);
            OnPropertyChanged(nameof(GridLayoutVersion));
        }

        var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
        var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

        // 内容实际撑开的格子范围（不含拖拽预览），供固定尺寸下限校验使用。
        PublishGridExtentIfChanged(maxCol + 1, maxRow + 1);

        // While a drag preview is showing, grow the canvas just enough to include the previewed
        // slot, so dropping at the right/bottom edge visibly extends the box by one cell and it
        // shrinks back as soon as the pointer moves off the edge (or the preview is hidden on
        // drop / leave). The edge threshold itself is anchored to the item grid (see
        // GetGridSlot), so this no longer oscillates continuously — at most a brief flicker when
        // the pointer sits right on the boundary.
        if (IsDragPreviewVisible)
        {
            maxCol = Math.Max(maxCol, _previewColumn);
            maxRow = Math.Max(maxRow, _previewRow);
        }

        if (positionsChanged)
        {
            foreach (var item in Items)
            {
                item.SetTempOffset(0, 0, LayoutSettings);
            }
        }

        if (IsFixedSize)
        {
            GridCanvasWidth = Math.Max(1, SizeMode.Columns) * LayoutSettings.ItemSlotWidth;
            GridCanvasHeight = Math.Max(1, SizeMode.Rows) * LayoutSettings.ItemSlotHeight;
        }
        else
        {
            GridCanvasWidth = Math.Max(1, maxCol + 1) * LayoutSettings.ItemSlotWidth;
            GridCanvasHeight = Math.Max(1, maxRow + 1) * LayoutSettings.ItemSlotHeight;
        }

        // 记录画布尺寸实际变化的时刻：GetGridSlot 在此后的极短窗口内冻结落点计算。
        if (GridCanvasWidth != _lastCanvasWidth || GridCanvasHeight != _lastCanvasHeight)
        {
            _lastCanvasWidth = GridCanvasWidth;
            _lastCanvasHeight = GridCanvasHeight;
            _lastCanvasSizeChangedUtc = DateTime.UtcNow;
        }

        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    private void PublishGridExtentIfChanged(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (_occupiedColumns == columns && _occupiedRows == rows)
        {
            return;
        }

        _occupiedColumns = columns;
        _occupiedRows = rows;
        OnPropertyChanged(nameof(OccupiedColumns));
        OnPropertyChanged(nameof(OccupiedRows));
        WeakReferenceMessenger.Default.Send(new BoxGridExtentChangedMessage(BoxId, columns, rows));
    }
}
