using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
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
    private string _statusText = "拖入文件";

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger,
        DesktopBoxLayoutSettings layoutSettings)
    {
        _box = box;
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
        _layoutSettings = layoutSettings;
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

    public double DragPreviewWidth => LayoutSettings.ItemSlotWidth;

    public double DragPreviewHeight => LayoutSettings.ItemSlotHeight;

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

    public (int Column, int Row) GetGridSlot(double x, double y)
    {
        var maxColumn = Math.Max(0, LayoutSettings.Columns - 1);
        var column = Math.Clamp((int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)), 0, maxColumn);
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));
        return (column, row);
    }

    public void ShowDragPreview(int column, int row)
    {
        var slot = NormalizeGridSlot(column, row);
        DragPreviewLeft = slot.Column * LayoutSettings.ItemSlotWidth;
        DragPreviewTop = slot.Row * LayoutSettings.ItemSlotHeight;
        IsDragPreviewVisible = true;
    }

    public void HideDragPreview()
    {
        IsDragPreviewVisible = false;
    }

    public async Task LoadAsync()
    {
        try
        {
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

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        await ImportPathsAsync(paths, null, null);
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths, int? startColumn, int? startRow)
    {
        var pathList = paths.ToArray();
        if (pathList.Length == 0 || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var imported = 0;
            var reservedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
            var nextColumn = startColumn ?? 0;
            var nextRow = startRow ?? 0;
            foreach (var path in pathList)
            {
                var slot = FindFirstFreeSlot(nextColumn, nextRow, reservedSlots);
                reservedSlots.Add(slot);
                await _drawerService.ImportPathAsync(BoxId, path, slot.Column, slot.Row);
                nextColumn = slot.Column + 1;
                nextRow = slot.Row;
                imported++;
            }

            await LoadAsync();
            StatusText = $"已收纳 {imported} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import into desktop box.");
            StatusText = exception.Message;
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

        UpdateGridCanvasSize();
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

    private (int Column, int Row) FindFirstFreeSlot(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots)
    {
        var slot = NormalizeGridSlot(startColumn, startRow);
        var column = slot.Column;
        var row = slot.Row;
        var maxColumn = Math.Max(0, LayoutSettings.Columns - 1);

        while (occupiedSlots.Contains((column, row)))
        {
            column++;
            if (column > maxColumn)
            {
                column = 0;
                row++;
            }
        }

        return (column, row);
    }

    private (int Column, int Row) NormalizeGridSlot(int column, int row)
    {
        var maxColumn = Math.Max(0, LayoutSettings.Columns - 1);
        return (Math.Clamp(column, 0, maxColumn), Math.Max(0, row));
    }

    private void UpdateGridCanvasSize()
    {
        var maxColumn = Items.Count == 0 ? LayoutSettings.Columns - 1 : Items.Max(item => item.GridColumn);
        var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

        GridCanvasWidth = Math.Max(1, Math.Max(LayoutSettings.Columns, maxColumn + 1)) * LayoutSettings.ItemSlotWidth;
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

