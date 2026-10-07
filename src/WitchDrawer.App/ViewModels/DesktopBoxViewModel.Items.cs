using WitchDrawer.Core.Localization;
using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的项目集合部分：项目加载、排序模式与格位分配。</summary>
public sealed partial class DesktopBoxViewModel
{
    private bool TryFindFreeSlotInFixedBounds(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots,
        out (int Column, int Row) slot)
    {
        var columns = _sizeMode.Columns;
        var rows = _sizeMode.Rows;
        var preferred = (
            Math.Clamp(startColumn, 0, columns - 1),
            Math.Clamp(startRow, 0, rows - 1));
        if (!occupiedSlots.Contains(preferred))
        {
            slot = preferred;
            return true;
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!occupiedSlots.Contains((column, row)))
                {
                    slot = (column, row);
                    return true;
                }
            }
        }

        slot = preferred;
        return false;
    }

    public async Task LoadAsync()
    {
        try
        {
            if (IsTodoBox)
            {
                Items.ReplaceAll([]);
                await LoadTodoItemsAsync();
                UpdateGridCanvasSize();
                return;
            }

            // Each desktop box owns its layout settings. The manager restores the preset
            // before the window is created so boxes can use different icon sizes.

            var items = await _drawerService.GetItemsAsync(BoxId);
            var isPixelated = IsPixelStyle;
            var existingById = Items.ToDictionary(item => item.Id);
            var nextItems = new List<DrawerItemViewModel>(items.Count);

            foreach (var item in items)
            {
                if (!existingById.TryGetValue(item.Id, out var itemViewModel)
                    || !itemViewModel.TryUpdateModel(item))
                {
                    itemViewModel = new DrawerItemViewModel(
                        item,
                        Name,
                        isPixelated,
                        GetIconPixelSize(isPixelated),
                        _logger);
                }

                itemViewModel.RequestIconSize(GetIconPixelSize(isPixelated));
                nextItems.Add(itemViewModel);
            }

            if (IsFreeSort)
            {
                // 自由排序：按持久化格位摆放（含无格位项目的空位分配）。
                var positions = ResolveItemPositions(items);
                foreach (var itemViewModel in nextItems)
                {
                    var itemPosition = positions[itemViewModel.Id];
                    itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                }

                Items.Synchronize(nextItems);
            }
            else
            {
                // 自动排序：按排序键行优先展示；不写库，自由布局不受污染。
                var ordered = await Task.Run(() => SortDrawerItems(nextItems, _drawerItemSortMode));
                var sortedPositions = AssignSortedGridPositions(ordered);
                foreach (var itemViewModel in ordered)
                {
                    var itemPosition = sortedPositions[itemViewModel.Id];
                    itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                }

                Items.Synchronize(ordered);
            }

            StatusText = Items.Count == 0 ? Strings.Get("DropFilesHere") : Strings.Get("Synced");
            UpdateGridCanvasSize();
            OnPropertyChanged(nameof(ItemCountLabel));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowFileEmptyState));
            RefreshDrawerPreview();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load desktop box.");
            StatusText = exception.Message;
        }
    }

    public void ReleaseHiddenWindowItems()
    {
        Items.ReplaceAll([]);
        DrawerPreviewItems.Clear();
        DrawerCoverTiles.Clear();
        DrawerSecondaryItems.ReplaceAll([]);
        TodoItems.Clear();
        UpdateGridCanvasSize();
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowFileEmptyState));
    }

    /// <summary>
    /// 当全局“图标名称（悬停提示）”模式切换时，刷新各文件条目以重绘其悬停提示文本。
    /// </summary>
    public void RefreshItemHoverDisplayTexts()
    {
        foreach (var item in Items)
        {
            item.RaiseHoverDisplayTextChanged();
        }

        foreach (var item in DrawerSecondaryItems)
        {
            item.RaiseHoverDisplayTextChanged();
        }
    }

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = Strings.Format("Opened", item.DisplayName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open desktop box item.");
            StatusText = exception.Message;
        }
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _drawerService.DeleteItemFromBoxAsync(BoxId, item.Id);
            await LoadAsync();
            StatusText = result.StatusMessage;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to delete desktop box item.");
            StatusText = exception.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 自动排序模式的格位分配：按排序后的顺序行优先填充。不写库——仅显示层。
    /// 自适应模式沿用当前内容列宽（至少 4 列）；固定模式 wrap 到 m 列并钳制在边界内。
    /// </summary>
    private Dictionary<Guid, (int Column, int Row)> AssignSortedGridPositions(
        IReadOnlyList<DrawerItemViewModel> orderedItems)
    {
        var wrapColumns = IsFixedSize
            ? Math.Max(1, _sizeMode.Columns)
            : Math.Max(4, _occupiedColumns);
        var positions = new Dictionary<Guid, (int Column, int Row)>(orderedItems.Count);
        for (var index = 0; index < orderedItems.Count; index++)
        {
            var column = index % wrapColumns;
            var row = index / wrapColumns;
            if (IsFixedSize)
            {
                // 超出容量的历史数据退化为边界内重叠（保持可见可选中）。
                column = Math.Min(column, _sizeMode.Columns - 1);
                row = Math.Min(row, _sizeMode.Rows - 1);
            }

            positions[orderedItems[index].Id] = (column, row);
        }

        return positions;
    }

    private Dictionary<Guid, (int Column, int Row)> ResolveItemPositions(IReadOnlyList<DrawerItem> items)
    {
        var positions = new Dictionary<Guid, (int Column, int Row)>();
        var usedSlots = new HashSet<(int Column, int Row)>();
        var nextColumn = 0;
        var nextRow = 0;
        var maxUsedColumn = 0;

        foreach (var item in items)
        {
            (int Column, int Row)? persisted = item.GridColumn >= 0 && item.GridRow >= 0
                ? (item.GridColumn.Value, item.GridRow.Value)
                : null;
            // 固定模式：越界的持久化格位（如经未做约束的入口导入）视为无格位，在边界内重排。
            if (persisted is { } persistedSlot
                && IsFixedSize
                && (persistedSlot.Column >= _sizeMode.Columns || persistedSlot.Row >= _sizeMode.Rows))
            {
                persisted = null;
            }

            (int Column, int Row) slot;
            if (persisted is { } validSlot && !usedSlots.Contains(validSlot))
            {
                slot = validSlot;
            }
            else if (IsFixedSize)
            {
                // 固定模式在 m×n 边界内找空位；满载时退化为钳制后的首选格
                // （项目重叠但保持可见可操作，优于渲染到窗口外永久丢失）。
                TryFindFreeSlotInFixedBounds(nextColumn, nextRow, usedSlots, out slot);
            }
            else
            {
                slot = FindFirstFreeSlot(
                    nextColumn,
                    nextRow,
                    usedSlots,
                    maxUsedColumn);
            }

            usedSlots.Add(slot);
            positions[item.Id] = slot;
            maxUsedColumn = Math.Max(maxUsedColumn, slot.Column);
            nextColumn = slot.Column + 1;
            nextRow = slot.Row;
        }

        return positions;
    }

    private (int Column, int Row) FindFirstFreeSlot(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots,
        int? knownMaxOccupiedColumn = null)
    {

        var column = Math.Max(0, startColumn);
        var row = Math.Max(0, startRow);
        var maxOccupiedColumn = knownMaxOccupiedColumn
            ?? (occupiedSlots.Count > 0 ? occupiedSlots.Max(slot => slot.Column) : 0);
        var wrapColumn = Math.Max(4, Math.Max(column, maxOccupiedColumn));

        while (occupiedSlots.Contains((column, row)))
        {
            column++;
            if (column > wrapColumn)
            {
                column = Math.Max(0, startColumn);
                row++;
            }
        }

        return (column, row);
    }

    private (int Column, int Row) NormalizeGridSlot(int column, int row)
    {
        return (Math.Max(0, column), Math.Max(0, row));
    }

    public async Task LoadSortModeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!SupportsSorting)
        {
            return;
        }

        var saved = await ReadSettingAsync(GetBoxSortModeSettingKey(BoxId), snapshot);
        if (saved is null && IsDrawerBox)
        {
            // 迁移抽屉盒旧的 DrawerSortMode: 设置值。
            saved = await ReadSettingAsync(GetDrawerSortModeSettingKey(BoxId), snapshot);
        }

        ApplyDrawerSortMode(
            Enum.TryParse<DrawerItemSortMode>(saved, ignoreCase: true, out var sortMode)
                ? sortMode
                : DrawerItemSortMode.Free);
    }

    /// <summary>
    /// 应用排序模式；返回是否有变化。变化时调用方应触发重新加载以重排显示。
    /// </summary>
    public bool ApplyDrawerSortMode(DrawerItemSortMode sortMode)
    {
        if (_drawerItemSortMode == sortMode)
        {
            return false;
        }

        _drawerItemSortMode = sortMode;
        OnPropertyChanged(nameof(DrawerItemSortMode));
        OnPropertyChanged(nameof(IsFreeSort));
        return true;
    }

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"{DrawerSortModeSettingPrefix}{boxId:N}";

    internal static string GetBoxSortModeSettingKey(Guid boxId) =>
        $"BoxSortMode:{boxId:N}";

    internal static IReadOnlyList<DrawerItemViewModel> SortDrawerItems(
        IReadOnlyList<DrawerItemViewModel> items,
        DrawerItemSortMode sortMode,
        Func<DrawerItemViewModel, DrawerItemSortMode, (long Size, DateTime ModifiedDateUtc)>? readMetadata = null)
    {
        if (sortMode == DrawerItemSortMode.Free)
        {
            return items;
        }

        var entries = items.Select(item => CreateDrawerSortEntry(item, sortMode, readMetadata)).ToArray();
        IOrderedEnumerable<DrawerSortEntry> ordered = sortMode switch
        {
            DrawerItemSortMode.Size => entries
                .OrderBy(entry => entry.Size)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            DrawerItemSortMode.ItemType => entries
                .OrderBy(entry => entry.ItemType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            DrawerItemSortMode.ModifiedDate => entries
                .OrderByDescending(entry => entry.ModifiedDateUtc)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => entries.OrderBy(
                entry => entry.Name,
                StringComparer.CurrentCultureIgnoreCase)
        };

        return ordered.Select(entry => entry.Item).ToArray();
    }

    private static DrawerSortEntry CreateDrawerSortEntry(
        DrawerItemViewModel item,
        DrawerItemSortMode sortMode,
        Func<DrawerItemViewModel, DrawerItemSortMode, (long Size, DateTime ModifiedDateUtc)>? readMetadata)
    {
        var path = item.PathLabel;
        try
        {
            var itemType = item.Model.ItemKind == ItemKind.Directory
                ? Strings.Get("Folder")
                : Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(itemType))
            {
                itemType = Strings.Get("File");
            }

            // 名称与类型只依赖模型及路径文本；慢盘/网络路径无需查询文件系统。
            var metadata = sortMode is DrawerItemSortMode.Size or DrawerItemSortMode.ModifiedDate
                ? (readMetadata ?? ReadDrawerSortMetadata)(item, sortMode)
                : (Size: long.MaxValue, ModifiedDateUtc: DateTime.MinValue);

            return new DrawerSortEntry(
                item,
                item.DisplayName,
                itemType,
                metadata.Size,
                metadata.ModifiedDateUtc);
        }
        catch
        {
            return new DrawerSortEntry(
                item,
                item.DisplayName,
                item.KindLabel,
                long.MaxValue,
                DateTime.MinValue);
        }
    }

    private static (long Size, DateTime ModifiedDateUtc) ReadDrawerSortMetadata(
        DrawerItemViewModel item,
        DrawerItemSortMode sortMode)
    {
        if (item.Model.ItemKind == ItemKind.Directory)
        {
            return (-1, sortMode == DrawerItemSortMode.ModifiedDate
                ? Directory.GetLastWriteTimeUtc(item.PathLabel)
                : DateTime.MinValue);
        }

        var fileInfo = new FileInfo(item.PathLabel);
        if (!fileInfo.Exists)
        {
            return (long.MaxValue, DateTime.MinValue);
        }

        return sortMode == DrawerItemSortMode.Size
            ? (fileInfo.Length, DateTime.MinValue)
            : (long.MaxValue, fileInfo.LastWriteTimeUtc);
    }
}
