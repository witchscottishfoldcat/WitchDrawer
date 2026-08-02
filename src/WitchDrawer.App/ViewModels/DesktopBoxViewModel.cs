using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
    private const double EdgeExpandThreshold = 14;
    private const string MappingViewModeSettingPrefix = "MappingViewMode:";
    private const string MappingListViewMode = "List";
    private const string MappingGridViewMode = "Grid";
    private const string DrawerCoverSizeSettingPrefix = "DrawerCoverSize:";
    private const string TitleVisibilitySettingPrefix = "BoxTitleVisible:";
    private const string LegacyDrawerTitleVisibilitySettingPrefix = "DrawerTitleVisible:";
    private const string DrawerSortModeSettingPrefix = "DrawerSortMode:";
    private const double DefaultDrawerCoverWidth = 180;
    private const double DefaultDrawerCoverHeight = 112;
    private const double MaximumDrawerCoverDimension = 720;
    private const double DrawerTitleHeightCompensation = 9;

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly DesktopBoxLayoutSettings _layoutSettings;
    private Box _box;
    private BoxVisualStyle _visualStyle;
    private bool _isBusy;
    private double _gridCanvasWidth;
    private double _gridCanvasHeight;
    private bool _isDragPreviewVisible;
    private double _dragPreviewLeft;
    private double _dragPreviewTop;
    private int _previewColumn;
    private int _previewRow;
    private string _statusText = "拖入文件";
    private bool _isDragOver;
    private bool _isMappingListMode;
    private string _newTodoTitle = string.Empty;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;
    private bool _isDrawerExpanded;
    private bool _isTitleVisible = true;
    private double _drawerCoverWidth = DefaultDrawerCoverWidth;
    private double _drawerCoverHeight = DefaultDrawerCoverHeight;
    private int _drawerCoverColumns = 3;
    private int _drawerCoverRows = 2;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Name;

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyle visualStyle,
        DesktopBoxLayoutSettings? layoutSettings = null)
    {
        _box = box;
        _visualStyle = visualStyle;
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        _layoutSettings = layoutSettings ?? new DesktopBoxLayoutSettings(box.Type == BoxType.Drawer);
        _layoutSettings.PropertyChanged += OnLayoutSettingsChanged;

        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        UseMappingGridModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: false));
        UseMappingListModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: true));
        AddTodoCommand = new AsyncRelayCommand(AddTodoAsync, CanAddTodo);
        ToggleTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(ToggleTodoAsync);
        ArchiveCompletedTodosCommand = new AsyncRelayCommand(ArchiveCompletedTodosAsync, CanArchiveCompletedTodos);
        DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(DeleteTodoAsync);
        UpdateGridCanvasSize();
        _ = LoadMappingViewModeAsync();
    }

    public DesktopBoxLayoutSettings LayoutSettings => _layoutSettings;

    public event EventHandler? ItemsChanged;

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<DrawerItemViewModel> DrawerPreviewItems { get; } = [];

    public ObservableCollection<DrawerCoverTileViewModel> DrawerCoverTiles { get; } = [];

    public ObservableCollection<DrawerItemViewModel> DrawerSecondaryItems { get; } = [];

    public ObservableCollection<TodoItemViewModel> TodoItems { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand UseMappingGridModeCommand { get; }

    public IAsyncRelayCommand UseMappingListModeCommand { get; }

    public IAsyncRelayCommand AddTodoCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> ToggleTodoCommand { get; }

    public IAsyncRelayCommand ArchiveCompletedTodosCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> DeleteTodoCommand { get; }

    public Guid BoxId => _box.Id;

    public string Name => _box.Name;

    public BoxType Type => _box.Type;

    public BoxVisualStyle VisualStyle => _visualStyle;

    public bool IsPixelStyle => VisualStyle == BoxVisualStyle.Pixel;

    public bool IsMappingBox => Type == BoxType.Mapping;

    public bool IsTodoBox => Type == BoxType.Todo;

    public bool IsDrawerBox => Type == BoxType.Drawer;

    public bool IsDrawerExpanded
    {
        get => IsDrawerBox && _isDrawerExpanded;
        set
        {
            if (SetProperty(ref _isDrawerExpanded, value))
            {
                OnPropertyChanged(nameof(IsDrawerCollapsed));
                OnPropertyChanged(nameof(IsHeaderVisible));
            }
        }
    }

    public bool IsDrawerCollapsed => IsDrawerBox && !IsDrawerExpanded;

    public bool IsTitleVisible => _isTitleVisible;

    public bool IsHeaderVisible => !IsDrawerCollapsed || IsTitleVisible;

    public double DrawerCoverWidth => _drawerCoverWidth;

    public double DrawerCoverHeight => _drawerCoverHeight;

    public double DrawerContentHeight => CalculateDrawerContentHeight(
        DrawerCoverHeight,
        IsTitleVisible);

    public int DrawerCoverColumns => _drawerCoverColumns;

    public int DrawerCoverRows => _drawerCoverRows;

    public int DrawerCoverCapacity => DrawerCoverColumns * DrawerCoverRows;

    public bool DrawerHasOverflow => Items.Count > DrawerCoverCapacity;

    public int DrawerDirectItemCount => CalculateDrawerDirectItemCount(
        Items.Count,
        DrawerCoverCapacity);

    public DrawerItemSortMode DrawerItemSortMode => _drawerItemSortMode;

    public int DrawerSecondaryColumns => CalculateDrawerSecondaryColumns(
        DrawerSecondaryItems.Count);

    public int DrawerSecondaryRows => CalculateDrawerSecondaryRows(
        DrawerSecondaryItems.Count,
        DrawerSecondaryColumns);

    public bool DrawerSecondaryHasScrollableOverflow => DrawerSecondaryRows > 5;

    public double DrawerSecondaryPanelWidth => Math.Clamp(
        (DrawerSecondaryColumns
            * (LayoutSettings.DrawerPrimaryIconFrameSize + 8))
        + 20,
        110,
        320);

    public double DrawerSecondaryPanelHeight => Math.Clamp(
        (Math.Min(5, DrawerSecondaryRows)
            * (LayoutSettings.DrawerPrimaryIconFrameSize + 8))
        + 20,
        96,
        320);

    public bool IsMappingListMode => IsMappingBox && _isMappingListMode;

    public bool IsGridMode => !IsMappingListMode;

    public string TypeLabel => _box.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "普通",
        BoxType.Mapping => "映射",
        BoxType.Todo => "待办",
        BoxType.Drawer => "抽屉",
        _ => "未知"
    };

    public string Description => _box.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "移动收纳",
        BoxType.Mapping => "路径映射",
        BoxType.Todo => "桌面待办",
        BoxType.Drawer => "点击展开",
        _ => string.Empty
    };

    public string ItemCountLabel => $"{(IsTodoBox ? TodoItems.Count : Items.Count)} 项";

    public bool IsEmpty => Items.Count == 0;

    public bool ShowFileEmptyState => !IsTodoBox && IsEmpty;

    public string NewTodoTitle
    {
        get => _newTodoTitle;
        set
        {
            if (SetProperty(ref _newTodoTitle, value))
            {
                AddTodoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int TodoRemainingCount => TodoItems.Count(todo => !todo.IsCompleted);

    public int TodoCompletedCount => TodoItems.Count(todo => todo.IsCompleted);

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

    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetProperty(ref _isDragOver, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void UpdateBox(Box box, BoxVisualStyle visualStyle)
    {
        _box = box;
        _visualStyle = visualStyle;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(VisualStyle));
        OnPropertyChanged(nameof(IsPixelStyle));
        OnPropertyChanged(nameof(IsMappingBox));
        OnPropertyChanged(nameof(IsTodoBox));
        OnPropertyChanged(nameof(IsDrawerBox));
        OnPropertyChanged(nameof(IsDrawerExpanded));
        OnPropertyChanged(nameof(IsDrawerCollapsed));
        OnPropertyChanged(nameof(IsTitleVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(DrawerContentHeight));
        OnPropertyChanged(nameof(IsMappingListMode));
        OnPropertyChanged(nameof(IsGridMode));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        AddTodoCommand.NotifyCanExecuteChanged();
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
        UpdateItemIconSizes();
    }

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);
        UpdateItemIconSizes();
    }

    public (int Column, int Row) GetGridSlot(
        double x,
        double y,
        double surfaceWidth = 0,
        double surfaceHeight = 0)
    {
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));

        // Edge expansion: when the pointer reaches the right/bottom edge of the *content*
        // grid, target a brand-new column/row so the box grows by one cell.
        //
        // The reference is the item-grid extent ((maxCol+1)*slotWidth), which stays constant
        // while dragging. Using the live window/IconList size here would create a feedback
        // loop: expanding grows the window, which moves the edge away from the pointer, which
        // un-expands, which shrinks the window... — the box would flicker at the threshold.
        if (surfaceWidth > 0 && surfaceHeight > 0)
        {
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
        if (IsMappingListMode)
        {
            IsDragPreviewVisible = false;
            return;
        }

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

    public (int Column, int Row) GetListDropSlot(Guid? movingItemId = null)
    {
        var maxRow = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => item.GridRow)
            .DefaultIfEmpty(-1)
            .Max();

        return (0, maxRow + 1);
    }

    public async Task LoadAsync()
    {
        try
        {
            if (IsTodoBox)
            {
                Items.Clear();
                await LoadTodoItemsAsync();
                UpdateGridCanvasSize();
                return;
            }

            // Each desktop box owns its layout settings. The manager restores the preset
            // before the window is created so boxes can use different icon sizes.

            var items = await _drawerService.GetItemsAsync(BoxId);
            var isPixelated = IsPixelStyle;
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
                    existing?.RequestIconSize(GetIconPixelSize(isPixelated));
                    continue;
                }

                var itemViewModel = new DrawerItemViewModel(
                    items[i],
                    Name,
                    isPixelated,
                    GetIconPixelSize(isPixelated),
                    _logger);
                var itemPosition = positions[items[i].Id];
                itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                Items.Insert(i, itemViewModel);
            }

            StatusText = Items.Count == 0 ? "拖入文件" : "已同步";
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

    private bool CanAddTodo()
    {
        return IsTodoBox && !IsBusy && !string.IsNullOrWhiteSpace(NewTodoTitle);
    }

    private async Task AddTodoAsync()
    {
        var title = NewTodoTitle;
        await RunTodoOperationAsync(async () =>
        {
            await _todoService.AddTodoAsync(BoxId, title);
            NewTodoTitle = string.Empty;
            StatusText = "已添加";
        });
    }

    private async Task ToggleTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return;
        }

        await RunTodoOperationAsync(async () =>
        {
            await _todoService.SetCompletedAsync(todo.Id, !todo.IsCompleted);
            StatusText = todo.IsCompleted ? "已恢复" : "已完成";
        });
    }

    private bool CanArchiveCompletedTodos()
    {
        return IsTodoBox && !IsBusy && TodoCompletedCount > 0;
    }

    private async Task ArchiveCompletedTodosAsync()
    {
        await RunTodoOperationAsync(async () =>
        {
            var archivedCount = await _todoService.ArchiveCompletedAsync(BoxId);
            StatusText = archivedCount == 0 ? "没有可归档事项" : $"已归档 {archivedCount} 项";
        });
    }

    private async Task DeleteTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return;
        }

        await RunTodoOperationAsync(async () =>
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            StatusText = "已删除";
        });
    }

    private async Task RunTodoOperationAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            AddTodoCommand.NotifyCanExecuteChanged();
            ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
            await operation();
            await LoadTodoItemsAsync();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to update todo box.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            AddTodoCommand.NotifyCanExecuteChanged();
            ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadTodoItemsAsync()
    {
        var todos = await _todoService.GetTodosAsync(BoxId);
        TodoItems.Clear();
        foreach (var todo in todos)
        {
            TodoItems.Add(new TodoItemViewModel(todo));
        }

        StatusText = TodoItems.Count == 0 ? "添加待办" : "已同步";
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(TodoRemainingCount));
        OnPropertyChanged(nameof(TodoCompletedCount));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
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

    private async Task LoadMappingViewModeAsync()
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var savedMode = await _drawerService.GetSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"));
            SetMappingListMode(string.Equals(savedMode, MappingListViewMode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load mapping view mode.");
        }
    }

    private async Task SetMappingViewModeAsync(bool useListMode)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            SetMappingListMode(useListMode);
            var mode = useListMode ? MappingListViewMode : MappingGridViewMode;
            await _drawerService.SetSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"), mode);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save mapping view mode.");
        }
    }

    private void SetMappingListMode(bool value)
    {
        if (SetProperty(ref _isMappingListMode, value, nameof(IsMappingListMode)))
        {
            OnPropertyChanged(nameof(IsGridMode));
            HideDragPreview();
            UpdateItemIconSizes();
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
            var result = await _drawerService.DeleteItemAsync(item.Id);
            await LoadAsync();
            StatusText = result.StatusMessage;
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

        foreach (var item in Items)
        {
            item.SetTempOffset(0, 0, LayoutSettings);
        }

        GridCanvasWidth = Math.Max(1, maxCol + 1) * LayoutSettings.ItemSlotWidth;
        GridCanvasHeight = Math.Max(1, maxRow + 1) * LayoutSettings.ItemSlotHeight;
        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    public void ResizeDrawerCover(double width, double height)
    {
        var normalized = NormalizeDrawerCoverSize(width, height, LayoutSettings.DrawerCoverCellSize);
        var widthChanged = SetProperty(
            ref _drawerCoverWidth,
            normalized.Width,
            nameof(DrawerCoverWidth));
        var heightChanged = SetProperty(
            ref _drawerCoverHeight,
            normalized.Height,
            nameof(DrawerCoverHeight));
        var columnsChanged = SetProperty(
            ref _drawerCoverColumns,
            normalized.Columns,
            nameof(DrawerCoverColumns));
        var rowsChanged = SetProperty(
            ref _drawerCoverRows,
            normalized.Rows,
            nameof(DrawerCoverRows));
        if (!widthChanged && !heightChanged && !columnsChanged && !rowsChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(DrawerCoverCapacity));
        OnPropertyChanged(nameof(DrawerHasOverflow));
        OnPropertyChanged(nameof(DrawerDirectItemCount));
        OnPropertyChanged(nameof(DrawerContentHeight));
        RefreshDrawerPreview();
    }

    public async Task LoadDrawerCoverSizeAsync()
    {
        if (!IsDrawerBox)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetDrawerCoverSizeSettingKey(BoxId));
        if (TryParseDrawerCoverSize(saved, out var width, out var height))
        {
            ResizeDrawerCover(width, height);
            return;
        }

        ResizeDrawerCover(DefaultDrawerCoverWidth, DefaultDrawerCoverHeight);
    }

    public async Task LoadTitleVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetTitleVisibilitySettingKey(BoxId));
        if (saved is null && IsDrawerBox)
        {
            saved = await _drawerService.GetSettingAsync(
                GetLegacyDrawerTitleVisibilitySettingKey(BoxId));
        }

        ApplyTitleVisibility(!bool.TryParse(saved, out var isVisible) || isVisible);
    }

    public void ApplyTitleVisibility(bool isVisible)
    {
        if (!SetProperty(
                ref _isTitleVisible,
                isVisible,
                nameof(IsTitleVisible)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(DrawerContentHeight));
    }

    public async Task LoadDrawerSortModeAsync()
    {
        if (!IsDrawerBox)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetDrawerSortModeSettingKey(BoxId));
        ApplyDrawerSortMode(
            Enum.TryParse<DrawerItemSortMode>(saved, ignoreCase: true, out var sortMode)
                ? sortMode
                : DrawerItemSortMode.Name);
    }

    public void ApplyDrawerSortMode(DrawerItemSortMode sortMode)
    {
        if (_drawerItemSortMode == sortMode)
        {
            return;
        }

        _drawerItemSortMode = sortMode;
        OnPropertyChanged(nameof(DrawerItemSortMode));
    }

    public Task SaveDrawerCoverSizeAsync()
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{DrawerCoverWidth:0.##},{DrawerCoverHeight:0.##}");
        return _drawerService.SetSettingAsync(GetDrawerCoverSizeSettingKey(BoxId), value);
    }

    internal static string GetDrawerCoverSizeSettingKey(Guid boxId) =>
        $"{DrawerCoverSizeSettingPrefix}{boxId:N}";

    internal static string GetTitleVisibilitySettingKey(Guid boxId) =>
        $"{TitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetLegacyDrawerTitleVisibilitySettingKey(Guid boxId) =>
        $"{LegacyDrawerTitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"{DrawerSortModeSettingPrefix}{boxId:N}";

    internal static bool TryParseDrawerCoverSize(
        string? value,
        out double width,
        out double height)
    {
        width = 0;
        height = 0;
        var parts = value?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            && double.IsFinite(width)
            && double.IsFinite(height)
            && width > 0
            && height > 0;
    }

    internal static (double Width, double Height, int Columns, int Rows) NormalizeDrawerCoverSize(
        double width,
        double height,
        double cellSize)
    {
        var normalizedCellSize = Math.Clamp(cellSize, 24, 120);
        const double surfaceInsets = DesktopBoxLayoutSettings.DrawerSurfaceInset * 2;
        var requestedWidth = double.IsFinite(width) ? width : DefaultDrawerCoverWidth;
        var requestedHeight = double.IsFinite(height) ? height : DefaultDrawerCoverHeight;
        var maximumCells = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellSize));
        var columns = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedWidth - surfaceInsets) / normalizedCellSize,
                MidpointRounding.AwayFromZero),
            1,
            maximumCells);
        var rows = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedHeight - surfaceInsets) / normalizedCellSize,
                MidpointRounding.AwayFromZero),
            1,
            maximumCells);
        if (columns * rows < 2 || (columns == 1 && rows == 2))
        {
            // The minimum drawer is always the established horizontal "1 + four previews"
            // shape. A 1x2 cover makes the primary and composite tiles stack vertically and
            // visually turns the already-finished drawer into a different component.
            columns = 2;
            rows = 1;
        }

        return (
            Math.Round((columns * normalizedCellSize) + surfaceInsets, 1),
            Math.Round((rows * normalizedCellSize) + surfaceInsets, 1),
            columns,
            rows);
    }

    internal static int CalculateDrawerDirectItemCount(int itemCount, int capacity)
    {
        var normalizedItemCount = Math.Max(0, itemCount);
        var normalizedCapacity = Math.Max(2, capacity);
        return normalizedItemCount > normalizedCapacity
            ? normalizedCapacity - 1
            : Math.Min(normalizedItemCount, normalizedCapacity);
    }

    internal static double CalculateDrawerContentHeight(
        double coverHeight,
        bool isTitleVisible) => Math.Max(
            1,
            coverHeight - (isTitleVisible ? DrawerTitleHeightCompensation : 0));

    public async Task ApplyDrawerItemSortAsync(DrawerItemSortMode sortMode)
    {
        var snapshot = Items.ToArray();
        var sortedItems = await Task.Run(() => SortDrawerItems(snapshot, sortMode));

        ApplyDrawerSortMode(sortMode);

        DrawerSecondaryItems.Clear();
        foreach (var item in sortedItems)
        {
            DrawerSecondaryItems.Add(item);
        }

        OnPropertyChanged(nameof(DrawerSecondaryColumns));
        OnPropertyChanged(nameof(DrawerSecondaryRows));
        OnPropertyChanged(nameof(DrawerSecondaryHasScrollableOverflow));
        OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
        OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
    }

    internal static IReadOnlyList<DrawerItemViewModel> SortDrawerItems(
        IReadOnlyList<DrawerItemViewModel> items,
        DrawerItemSortMode sortMode)
    {
        var entries = items.Select(CreateDrawerSortEntry).ToArray();
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

    internal static int CalculateDrawerSecondaryColumns(int itemCount) => Math.Clamp(
        (int)Math.Ceiling(Math.Sqrt(Math.Max(1, itemCount))),
        2,
        5);

    internal static int CalculateDrawerSecondaryRows(int itemCount, int columns) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(1, itemCount) / (double)Math.Max(1, columns)));

    private static DrawerSortEntry CreateDrawerSortEntry(DrawerItemViewModel item)
    {
        var path = item.PathLabel;
        try
        {
            if (item.Model.ItemKind == ItemKind.Directory)
            {
                return new DrawerSortEntry(
                    item,
                    item.DisplayName,
                    "文件夹",
                    -1,
                    Directory.GetLastWriteTimeUtc(path));
            }

            var fileInfo = new FileInfo(path);
            var itemType = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(itemType))
            {
                itemType = "文件";
            }

            return new DrawerSortEntry(
                item,
                item.DisplayName,
                itemType,
                fileInfo.Exists ? fileInfo.Length : long.MaxValue,
                fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue);
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

    private sealed record DrawerSortEntry(
        DrawerItemViewModel Item,
        string Name,
        string ItemType,
        long Size,
        DateTime ModifiedDateUtc);

    private void RefreshDrawerPreview()
    {
        DrawerCoverTiles.Clear();
        DrawerPreviewItems.Clear();
        var directItemCount = DrawerDirectItemCount;
        for (var index = 0; index < directItemCount; index++)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.ForItem(Items[index]));
        }

        if (DrawerHasOverflow)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.Expand());
            foreach (var item in Items.Skip(directItemCount).Take(4))
            {
                DrawerPreviewItems.Add(item);
            }
        }
    }

    private void OnLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.UpdateCanvasPosition(LayoutSettings);
        }

        UpdateItemIconSizes();
        UpdateGridCanvasSize();
        if (IsDrawerBox && e.PropertyName is nameof(DesktopBoxLayoutSettings.CurrentPreset))
        {
            ResizeDrawerCover(
                (DrawerCoverColumns * LayoutSettings.DrawerCoverCellSize)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2),
                (DrawerCoverRows * LayoutSettings.DrawerCoverCellSize)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2));
            OnPropertyChanged(nameof(DrawerCoverCapacity));
            OnPropertyChanged(nameof(DrawerHasOverflow));
            OnPropertyChanged(nameof(DrawerDirectItemCount));
            OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
            OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
            RefreshDrawerPreview();
        }
    }

    private void UpdateItemIconSizes()
    {
        var iconPixelSize = GetIconPixelSize(IsPixelStyle);
        foreach (var item in Items)
        {
            item.RequestIconSize(iconPixelSize);
        }
    }

    private int GetIconPixelSize(bool isPixelated)
    {
        var displaySizeDip = IsMappingListMode
            ? LayoutSettings.MappingListIconSize
            : IsDrawerBox
                ? Math.Max(LayoutSettings.IconSize, LayoutSettings.DrawerPrimaryIconSize)
                : LayoutSettings.IconSize;

        return DpiAwareIconSize.Calculate(
            displaySizeDip,
            displaySizeDip,
            _iconDpiScaleX,
            _iconDpiScaleY,
            isPixelated);
    }

    private static double NormalizeDpiScale(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }
}
