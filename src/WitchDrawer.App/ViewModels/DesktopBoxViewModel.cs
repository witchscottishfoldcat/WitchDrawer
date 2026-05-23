using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
    private const double EdgeExpandThreshold = 14;

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly DesktopBoxLayoutSettings _layoutSettings;
    private Box _box;
    private bool _isBusy;
    private double _gridCanvasWidth;
    private double _gridCanvasHeight;
    private bool _isDragPreviewVisible;
    private double _dragPreviewLeft;
    private double _dragPreviewTop;
    private int _previewColumn;
    private int _previewRow;
    private string _statusText = "拖入文件";

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger,
        DesktopBoxLayoutSettings? layoutSettings = null)
    {
        _box = box;
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
        _layoutSettings = layoutSettings ?? new DesktopBoxLayoutSettings();
        _layoutSettings.PropertyChanged += OnLayoutSettingsChanged;

        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        UpdateGridCanvasSize();
    }

    public DesktopBoxLayoutSettings LayoutSettings => _layoutSettings;

    public event EventHandler? ItemsChanged;

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public Guid BoxId => _box.Id;

    public string Name => _box.Name;

    public BoxType Type => _box.Type;

    public string TypeLabel => _box.Type switch
    {
        BoxType.Normal => "普通",
        BoxType.Mapping => "映射",
        BoxType.Pixel => "像素",
        _ => "未知"
    };

    public string Description => _box.Type switch
    {
        BoxType.Normal => "移动收纳",
        BoxType.Mapping => "路径映射",
        BoxType.Pixel => "像素收纳",
        _ => string.Empty
    };

    public string ItemCountLabel => $"{Items.Count} 项";

    public bool IsEmpty => Items.Count == 0;

    public double GridCanvasWidth
    {
        get => _gridCanvasWidth;
        private set => SetProperty(ref _gridCanvasWidth, value);
    }

    public double GridCanvasHeight
    {
        get => _gridCanvasHeight;
        private set => SetProperty(ref _gridCanvasHeight, value);
    }

    public bool IsDragPreviewVisible
    {
        get => _isDragPreviewVisible;
        private set => SetProperty(ref _isDragPreviewVisible, value);
    }

    public double DragPreviewLeft
    {
        get => _dragPreviewLeft;
        private set => SetProperty(ref _dragPreviewLeft, value);
    }

    public double DragPreviewTop
    {
        get => _dragPreviewTop;
        private set => SetProperty(ref _dragPreviewTop, value);
    }

    public double DragPreviewWidth => Math.Max(1, LayoutSettings.ItemSlotWidth - (LayoutSettings.ItemSpacing * 2));

    public double DragPreviewHeight => Math.Max(1, LayoutSettings.ItemSlotHeight - (LayoutSettings.ItemSpacing * 2));

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void UpdateBox(Box box)
    {
        _box = box;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public (int Column, int Row) GetGridSlot(
        double x,
        double y,
        double surfaceWidth = 0,
        double surfaceHeight = 0)
    {
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));
        if (surfaceWidth > 0 && surfaceHeight > 0)
        {
            var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
            var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

            if (x >= surfaceWidth - EdgeExpandThreshold)
            {
                column = Math.Max(column, maxCol + 1);
            }

            if (y >= surfaceHeight - EdgeExpandThreshold)
            {
                row = Math.Max(row, maxRow + 1);
            }
        }

        return (column, row);
    }

    public void UpdateDragPreview(double x, double y)
    {
        IsDragPreviewVisible = true;
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));
        _previewColumn = column;
        _previewRow = row;
    }

    public void ShowDragPreview(int column, int row)
    {
        _previewColumn = column;
        _previewRow = row;
        IsDragPreviewVisible = true;
        UpdateGridCanvasSize();

        DragPreviewLeft = (column * LayoutSettings.ItemSlotWidth) + LayoutSettings.ItemSpacing;
        DragPreviewTop = (row * LayoutSettings.ItemSlotHeight) + LayoutSettings.ItemSpacing;
    }

    public void HideDragPreview()
    {
        IsDragPreviewVisible = false;
        _previewColumn = 0;
        _previewRow = 0;
        UpdateGridCanvasSize();
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

    public async Task LoadAsync()
    {
        try
        {
            // Layout density is global (shared DesktopBoxLayoutSettings, controlled from
            // Settings). Boxes intentionally do NOT apply a per-box preset: every box shares
            // one settings instance, so re-applying each box's own preset on load made boxes
            // fight over the slot size and the windows visibly oscillated on every reload.

            var items = await _drawerService.GetItemsAsync(BoxId);
            var isPixelated = Type == BoxType.Pixel;
            var positions = ResolveItemPositions(items);

            var existingIds = new HashSet<Guid>();
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                existingIds.Add(Items[i].Id);
            }

            var newIds = items.Select(i => i.Id).ToHashSet();

            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(Items[i].Id))
                {
                    Items.RemoveAt(i);
                }
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (existingIds.Contains(items[i].Id))
                {
                    var existing = Items.FirstOrDefault(x => x.Id == items[i].Id);
                    var position = positions[items[i].Id];
                    existing?.SetGridPosition(position.Column, position.Row, LayoutSettings);
                    existing?.ReloadIconIfNeeded();
                    continue;
                }

                var itemViewModel = new DrawerItemViewModel(items[i], Name, isPixelated);
                var itemPosition = positions[items[i].Id];
                itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                Items.Insert(i, itemViewModel);
            }

            await NormalizeGridAndSaveAsync();

            StatusText = Items.Count == 0 ? "拖入文件" : "已同步";
            UpdateGridCanvasSize();
            OnPropertyChanged(nameof(ItemCountLabel));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load desktop box.");
            StatusText = exception.Message;
        }
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

        try
        {
            IsBusy = true;
            var importedIds = new List<Guid>(pathList.Length);
            var reservedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
            var nextColumn = startColumn ?? 0;
            var nextRow = startRow ?? 0;
            foreach (var path in pathList)
            {
                var slot = FindFirstFreeSlot(nextColumn, nextRow, reservedSlots);
                reservedSlots.Add(slot);
                var importedItem = await _drawerService.ImportPathAsync(BoxId, path, slot.Column, slot.Row);
                importedIds.Add(importedItem.Id);
                nextColumn = slot.Column + 1;
                nextRow = slot.Row;
            }

            await LoadAsync();
            await NormalizeGridAndSaveAsync();
            StatusText = $"已收纳 {importedIds.Count} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return importedIds;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import into desktop box.");
            StatusText = exception.Message;
            return Array.Empty<Guid>();
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
                await MoveItemWithinBoxAsync(currentItem, targetColumn, targetRow);
            }
            else
            {
                var occupiedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
                var targetSlot = FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
                await _drawerService.MoveItemToBoxAsync(itemId, BoxId, targetSlot.Column, targetSlot.Row);
                await LoadAsync();
                movedAcrossBoxes = true;
            }

            if (movedAcrossBoxes)
            {
                await NormalizeGridAndSaveAsync();
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
            await LoadAsync();
            await NormalizeGridAndSaveAsync();
            StatusText = $"Moved to desktop: {Path.GetFileName(exportedPath)}";
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

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = $"已打开 {item.DisplayName}";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open desktop box item.");
            StatusText = exception.Message;
        }
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _drawerService.DeleteItemAsync(item.Id, _trash);
            await LoadAsync();
            await NormalizeGridAndSaveAsync();
            StatusText = $"已移除 {item.DisplayName}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to delete desktop box item.");
            StatusText = exception.Message;
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
        var availableSlot = FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
        targetColumn = availableSlot.Column;
        targetRow = availableSlot.Row;

        await _drawerService.UpdateItemGridPositionAsync(item.Id, targetColumn, targetRow);
        item.SetGridPosition(targetColumn, targetRow, LayoutSettings);
        
        await NormalizeGridAndSaveAsync();
    }

    private Dictionary<Guid, (int Column, int Row)> ResolveItemPositions(IReadOnlyList<DrawerItem> items)
    {
        var positions = new Dictionary<Guid, (int Column, int Row)>();
        var usedSlots = new HashSet<(int Column, int Row)>();
        var nextColumn = 0;
        var nextRow = 0;

        foreach (var item in items)
        {
            (int Column, int Row) slot;
            if (item.GridColumn >= 0 && item.GridRow >= 0)
            {
                slot = (item.GridColumn.Value, item.GridRow.Value);
                if (usedSlots.Contains(slot))
                {
                    slot = FindFirstFreeSlot(nextColumn, nextRow, usedSlots);
                }
            }
            else
            {
                slot = FindFirstFreeSlot(nextColumn, nextRow, usedSlots);
            }

            usedSlots.Add(slot);
            positions[item.Id] = slot;
            nextColumn = slot.Column + 1;
            nextRow = slot.Row;
        }

        return positions;
    }

    private async Task NormalizeGridAndSaveAsync()
    {
        if (Items.Count == 0) return;
        var minCol = Items.Min(i => i.GridColumn);
        var minRow = Items.Min(i => i.GridRow);

        if (minCol != 0 || minRow != 0)
        {
            foreach (var item in Items)
            {
                var newCol = item.GridColumn - minCol;
                var newRow = item.GridRow - minRow;
                item.SetGridPosition(newCol, newRow, LayoutSettings);
                await _drawerService.UpdateItemGridPositionAsync(item.Id, newCol, newRow);
            }
        }
        
        UpdateGridCanvasSize();
    }

    private (int Column, int Row) FindFirstFreeSlot(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots)
    {

        var column = Math.Max(0, startColumn);
        var row = Math.Max(0, startRow);
        var maxOccupiedColumn = occupiedSlots.Count > 0 ? occupiedSlots.Max(s => s.Column) : 0;
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

    private void UpdateGridCanvasSize()
    {
        var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
        var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

        // While a drag preview is showing, grow the canvas to include the previewed slot so
        // dropping at the right/bottom edge visibly extends the box by one cell (and shrinks
        // back when the pointer leaves). This is a deliberate interaction, distinct from the
        // earlier cross-box preset oscillation that caused unwanted jitter.
        if (IsDragPreviewVisible)
        {
            maxCol = Math.Max(maxCol, _previewColumn);
            maxRow = Math.Max(maxRow, _previewRow);
        }

        foreach (var item in Items)
        {
            item.SetTempOffset(0, 0, LayoutSettings);
        }

        GridCanvasWidth = Math.Max(1, maxCol + 1) * LayoutSettings.ItemSlotWidth;
        GridCanvasHeight = Math.Max(1, maxRow + 1) * LayoutSettings.ItemSlotHeight;
        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    private void OnLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.UpdateCanvasPosition(LayoutSettings);
        }

        UpdateGridCanvasSize();
    }
}

