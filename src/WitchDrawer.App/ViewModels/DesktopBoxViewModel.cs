using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
    private const double DrawerSecondaryPanelChrome = 20;
    private const double MaximumDrawerSecondaryPanelDimension = 320;
    private const double EdgeExpandThreshold = 14;
    private const double VisibleHeaderRowHeight = 24;
    private const double HiddenGridContentInset = 6;
    private const string MappingViewModeSettingPrefix = "MappingViewMode:";
    private const string MappingListViewMode = "List";
    private const string MappingGridViewMode = "Grid";
    private const string DrawerCoverSizeSettingPrefix = "DrawerCoverSize:";
    private const string TitleVisibilitySettingPrefix = "BoxTitleVisible:";
    private const string LegacyDrawerTitleVisibilitySettingPrefix = "DrawerTitleVisible:";
    private const string FileNameVisibilitySettingPrefix = "BoxFileNameVisible:";
    private const string RollUpSettingPrefix = "BoxRolledUp:";
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
    private DateTime _lastCanvasSizeChangedUtc = DateTime.MinValue;
    private double _lastCanvasWidth = double.NaN;
    private double _lastCanvasHeight = double.NaN;
    private double _gridCanvasHeight;
    private bool _isDragPreviewVisible;
    private double _dragPreviewLeft;
    private double _dragPreviewTop;
    private double? _dragPreviewWidthOverride;
    private double? _dragPreviewHeightOverride;
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
    private bool _isFileNameVisible;
    private bool _isRolledUp;
    private double _drawerCoverWidth = DefaultDrawerCoverWidth;
    private double _drawerCoverHeight = DefaultDrawerCoverHeight;
    private int _drawerCoverColumns = 3;
    private int _drawerCoverRows = 2;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Free;
    private BoxSizeModeState _sizeMode = BoxSizeModeState.Adaptive;
    private int _occupiedColumns = 1;
    private int _occupiedRows = 1;

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

    /// <summary>供窗口层包装 fire-and-forget 任务时记录异常。</summary>
    internal IAppLogger Logger => _logger;

    public void ShowFileMissingNotice(DrawerItemViewModel item)
    {
        _logger.Info($"Context menu skipped: source path for item '{item.DisplayName}' no longer exists.");
        StatusText = $"文件不存在：{item.DisplayName}";
    }

    public void ShowContextMenuFailure(DrawerItemViewModel item, Exception exception)
    {
        _logger.Error(exception, $"Failed to show context menu for '{item.DisplayName}'.");
        StatusText = $"菜单打开失败：{exception.Message}";
    }

    public void ReportItemContextAction(string message)
    {
        StatusText = message;
    }

    public event EventHandler? ItemsChanged;

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<DrawerItemViewModel> DrawerPreviewItems { get; } = [];

    public ObservableCollection<DrawerCoverTileViewModel> DrawerCoverTiles { get; } = [];

    public ResettableObservableCollection<DrawerItemViewModel> DrawerSecondaryItems { get; } = [];

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

    /// <summary>
    /// 固定 m×n 格尺寸仅适用于普通网格收纳盒；其余盒型始终自适应。
    /// </summary>
    public bool SupportsFixedSize => Type is BoxType.Normal or BoxType.Pixel;

    public BoxSizeModeState SizeMode => _sizeMode;

    public bool IsFixedSize => SupportsFixedSize && _sizeMode.IsFixed;

    /// <summary>
    /// 网格视口宽度：固定模式下按 m×n 格物理尺寸 + 共享 chrome 预留渲染，
    /// 与自适应模式物理尺寸像素级对齐；自适应模式下为 NaN（Auto 贴合内容）。
    /// </summary>
    public double GridViewportWidth => IsFixedSize
        ? (SizeMode.Columns * LayoutSettings.ItemSlotWidth) + DesktopBoxLayoutSettings.GridViewportFixedChromeInset
        : double.NaN;

    public double GridViewportHeight => IsFixedSize
        ? (SizeMode.Rows * LayoutSettings.ItemSlotHeight) + DesktopBoxLayoutSettings.GridViewportFixedChromeInset
        : double.NaN;

    public int OccupiedColumns => _occupiedColumns;

    public int OccupiedRows => _occupiedRows;

    public bool IsDrawerExpanded
    {
        get => IsDrawerBox && _isDrawerExpanded;
        set
        {
            if (SetProperty(ref _isDrawerExpanded, value))
            {
                OnPropertyChanged(nameof(IsDrawerCollapsed));
                OnPropertyChanged(nameof(IsHeaderVisible));
                OnPropertyChanged(nameof(HeaderRowHeight));
                OnPropertyChanged(nameof(ShowFileEmptyState));
            }
        }
    }

    public bool IsDrawerCollapsed => IsDrawerBox && !IsDrawerExpanded;

    public bool IsTitleVisible => _isTitleVisible;

    public bool IsFileNameVisible => _isFileNameVisible;

    public bool SupportsRollUp => Type is BoxType.Normal or BoxType.Pixel or BoxType.Mapping;

    public bool IsRolledUp => SupportsRollUp && _isRolledUp;

    public bool IsHeaderTitleVisible => IsTitleVisible || IsRolledUp;

    public bool IsHeaderVisible => ShouldShowHeader(
        IsDrawerBox,
        IsDrawerExpanded,
        IsTitleVisible,
        IsRolledUp);

    public GridLength ContentRowHeight => IsRolledUp
        ? new GridLength(0)
        : new GridLength(1, GridUnitType.Star);

    public double HeaderRowHeight => CalculateHeaderRowHeight(
        IsHeaderVisible,
        IsDrawerBox,
        IsMappingListMode,
        LayoutSettings.MappingListMargin.Top,
        LayoutSettings.MappingListMargin.Bottom);

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

    public bool DrawerSecondaryHasScrollableOverflow => ShouldScrollDrawerSecondary(
        DrawerSecondaryRows,
        LayoutSettings.ItemSlotHeight);

    public double DrawerSecondaryPanelWidth => Math.Clamp(
        (DrawerSecondaryColumns
            * LayoutSettings.ItemSlotWidth)
        + DrawerSecondaryPanelChrome,
        110,
        MaximumDrawerSecondaryPanelDimension);

    public double DrawerSecondaryPanelHeight => Math.Clamp(
        (Math.Min(5, DrawerSecondaryRows)
            * LayoutSettings.ItemSlotHeight)
        + DrawerSecondaryPanelChrome,
        96,
        MaximumDrawerSecondaryPanelDimension);

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

    public bool ShowFileEmptyState => ShouldShowFileEmptyState(
        IsTodoBox,
        IsEmpty,
        IsDrawerCollapsed);

    internal static bool ShouldShowFileEmptyState(
        bool isTodoBox,
        bool isEmpty,
        bool isDrawerCollapsed) =>
        !isTodoBox && isEmpty && !isDrawerCollapsed;

    internal static bool ShouldShowGridDragPreview(
        bool isMappingListMode,
        bool isDrawerCollapsed) =>
        !isMappingListMode && !isDrawerCollapsed;

    internal static bool ShouldShowHeader(
        bool isDrawerBox,
        bool isDrawerExpanded,
        bool isTitleVisible,
        bool isRolledUp) =>
        isRolledUp || isTitleVisible || (isDrawerBox && isDrawerExpanded);

    internal static double CalculateHeaderRowHeight(
        bool isHeaderVisible,
        bool isDrawerBox,
        bool isMappingListMode,
        double contentTopMargin,
        double contentBottomMargin)
    {
        if (isHeaderVisible)
        {
            return VisibleHeaderRowHeight;
        }

        if (isDrawerBox)
        {
            return 0;
        }

        return isMappingListMode
            ? Math.Max(0, contentBottomMargin - contentTopMargin)
            : HiddenGridContentInset;
    }

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

    public double DragPreviewWidth => _dragPreviewWidthOverride
        ?? Math.Max(1, LayoutSettings.ItemSlotWidth - (LayoutSettings.ItemSpacing * 2));

    public double DragPreviewHeight => _dragPreviewHeightOverride
        ?? Math.Max(1, LayoutSettings.ItemSlotHeight - (LayoutSettings.ItemSpacing * 2));

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
        OnPropertyChanged(nameof(IsFileNameVisible));
        OnPropertyChanged(nameof(SupportsRollUp));
        OnPropertyChanged(nameof(IsRolledUp));
        OnPropertyChanged(nameof(IsHeaderTitleVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(ContentRowHeight));
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

        _previewColumn = column;
        _previewRow = row;
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
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
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
        UpdateGridCanvasSize();
    }

    // Free-form preview used by the collapsed drawer cover: the frame is placed
    // directly over the cover cell the dropped item will occupy, in the preview
    // canvas' coordinate space, instead of using item-grid slot math.
    public void ShowDragPreviewAt(double left, double top, double width, double height)
    {
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
    /// 固定模式下的总格数；自适应模式视为无限。
    /// </summary>
    public int FixedCapacity => IsFixedSize ? _sizeMode.Columns * _sizeMode.Rows : int.MaxValue;

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
                if (!existingById.TryGetValue(item.Id, out var itemViewModel))
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

                Items.ReplaceAll(nextItems);
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

                Items.ReplaceAll(ordered.ToList());
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
                // 排序模式：盒内拖动不换位（显示顺序由排序键决定），落放为空操作。
                if (IsFreeSort)
                {
                    await MoveItemWithinBoxAsync(currentItem, targetColumn, targetRow);
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
            ShellChangeNotifier.NotifyFolderItemCreated(
                exportedPath,
                item.Model.ItemKind == ItemKind.Directory);
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
            OnPropertyChanged(nameof(HeaderRowHeight));
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

    private void UpdateGridCanvasSize()
    {
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

        foreach (var item in Items)
        {
            item.SetTempOffset(0, 0, LayoutSettings);
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

    /// <summary>
    /// 应用尺寸模式（不触发持久化；持久化由设置页 ViewModel 负责）。
    /// </summary>
    public void ApplySizeMode(BoxSizeModeState state)
    {
        var normalized = SupportsFixedSize && state.IsFixed
            ? new BoxSizeModeState(
                true,
                BoxSizeModeState.ClampColumns(state.Columns),
                BoxSizeModeState.ClampRows(state.Rows))
            : BoxSizeModeState.Adaptive;
        if (_sizeMode == normalized)
        {
            return;
        }

        _sizeMode = normalized;
        OnPropertyChanged(nameof(SizeMode));
        OnPropertyChanged(nameof(IsFixedSize));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        OnPropertyChanged(nameof(FixedCapacity));
        UpdateGridCanvasSize();
    }

    internal async Task LoadSizeModeAsync()
    {
        var saved = await _drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(BoxId));
        ApplySizeMode(BoxSizeModeState.Parse(saved));
    }

    public void ResizeDrawerCover(double width, double height)
    {
        var normalized = NormalizeDrawerCoverSize(
            width,
            height,
            LayoutSettings.DrawerCoverCellWidth,
            LayoutSettings.DrawerCoverCellHeight);
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
        OnPropertyChanged(nameof(IsHeaderTitleVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(DrawerContentHeight));
    }

    public async Task LoadRollUpStateAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetRollUpSettingKey(BoxId));
        ApplyRollUpState(bool.TryParse(saved, out var isRolledUp) && isRolledUp);
    }

    internal void ApplyRollUpState(bool isRolledUp)
    {
        if (!SetProperty(ref _isRolledUp, SupportsRollUp && isRolledUp, nameof(IsRolledUp)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeaderTitleVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(ContentRowHeight));
    }

    public async Task SaveRollUpStateAsync()
    {
        try
        {
            await _drawerService.SetSettingAsync(GetRollUpSettingKey(BoxId), IsRolledUp.ToString());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop box roll-up state.");
        }
    }

    public async Task LoadFileNameVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetFileNameVisibilitySettingKey(BoxId));
        ApplyFileNameVisibility(bool.TryParse(saved, out var isVisible) && isVisible);
    }

    public void ApplyFileNameVisibility(bool isVisible)
    {
        LayoutSettings.IsFileNameVisible = isVisible;
        SetProperty(
            ref _isFileNameVisible,
            isVisible,
            nameof(IsFileNameVisible));
    }

    public async Task LoadSortModeAsync()
    {
        if (!SupportsSorting)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetBoxSortModeSettingKey(BoxId));
        if (saved is null && IsDrawerBox)
        {
            // 迁移抽屉盒旧的 DrawerSortMode: 设置值。
            saved = await _drawerService.GetSettingAsync(GetDrawerSortModeSettingKey(BoxId));
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

    /// <summary>
    /// 自由排序：显示顺序 = 格位/导入顺序（网格盒可拖拽摆放）。
    /// 非自由模式：显示顺序由排序键决定，盒内拖拽换位与格位写入均被禁用，
    /// 因此切回自由时自由布局原样恢复（天然有记忆）。
    /// </summary>
    public bool IsFreeSort => _drawerItemSortMode == DrawerItemSortMode.Free;

    /// <summary>
    /// 排序（自由/名称/大小/类型/修改日期）适用于所有收纳类盒型；待办盒有自己的排序语义。
    /// </summary>
    public bool SupportsSorting => Type is BoxType.Normal or BoxType.Pixel or BoxType.Mapping or BoxType.Drawer;

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

    internal static string GetFileNameVisibilitySettingKey(Guid boxId) =>
        $"{FileNameVisibilitySettingPrefix}{boxId:N}";

    internal static string GetRollUpSettingKey(Guid boxId) =>
        $"{RollUpSettingPrefix}{boxId:N}";

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"{DrawerSortModeSettingPrefix}{boxId:N}";

    internal static string GetBoxSortModeSettingKey(Guid boxId) =>
        $"BoxSortMode:{boxId:N}";

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
        double cellSize) => NormalizeDrawerCoverSize(width, height, cellSize, cellSize);

    internal static (double Width, double Height, int Columns, int Rows) NormalizeDrawerCoverSize(
        double width,
        double height,
        double cellWidth,
        double cellHeight)
    {
        var normalizedCellWidth = Math.Clamp(cellWidth, 24, 120);
        var normalizedCellHeight = Math.Clamp(cellHeight, 24, 136);
        const double surfaceInsets = DesktopBoxLayoutSettings.DrawerSurfaceInset * 2;
        var requestedWidth = double.IsFinite(width) ? width : DefaultDrawerCoverWidth;
        var requestedHeight = double.IsFinite(height) ? height : DefaultDrawerCoverHeight;
        var maximumColumns = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellWidth));
        var maximumRows = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellHeight));
        var columns = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedWidth - surfaceInsets) / normalizedCellWidth,
                MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        var rows = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedHeight - surfaceInsets) / normalizedCellHeight,
                MidpointRounding.AwayFromZero),
            1,
            maximumRows);
        if (columns * rows < 2 || (columns == 1 && rows == 2))
        {
            // The minimum drawer is always the established horizontal "1 + four previews"
            // shape. A 1x2 cover makes the primary and composite tiles stack vertically and
            // visually turns the already-finished drawer into a different component.
            columns = 2;
            rows = 1;
        }

        return (
            Math.Round((columns * normalizedCellWidth) + surfaceInsets, 1),
            Math.Round((rows * normalizedCellHeight) + surfaceInsets, 1),
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

    /// <summary>
    /// 展开抽屉二级弹窗前调用：弹窗只展示外层封面装不下的溢出项（顺序与盒内显示顺序
    /// Items，已按排序模式排好保持一致），避免封面已显示的图标在弹窗里重复出现。
    /// </summary>
    public void SyncDrawerSecondaryFromItems()
    {
        // 封面已占据前 DrawerDirectItemCount 个位置；弹窗只承接其后的溢出项。
        // DrawerDirectItemCount 在有溢出时为 封面容量-1（留一格给展开按钮），所以这里的
        // Skip 结果必非空；无溢出时根本没有展开按钮，不会走到这里。
        var overflowItems = Items.Skip(DrawerDirectItemCount).ToArray();
        DrawerSecondaryItems.ReplaceAll(overflowItems);

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
            // 自由排序：保持原顺序（格位/导入序），排序键不参与。
            DrawerItemSortMode.Free => entries.OrderBy(entry => 0),
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

    internal static bool ShouldScrollDrawerSecondary(int rows, double cellHeight) =>
        Math.Max(1, rows) * Math.Max(1, cellHeight)
        > MaximumDrawerSecondaryPanelDimension - DrawerSecondaryPanelChrome;

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
        if (e.PropertyName is nameof(DesktopBoxLayoutSettings.DrawerCoverCellWidth)
            or nameof(DesktopBoxLayoutSettings.DrawerCoverCellHeight)
            or nameof(DesktopBoxLayoutSettings.DrawerCoverCellSize))
        {
            return;
        }

        foreach (var item in Items)
        {
            item.UpdateCanvasPosition(LayoutSettings);
        }

        UpdateItemIconSizes();
        UpdateGridCanvasSize();
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        if (IsDrawerBox
            && e.PropertyName is nameof(DesktopBoxLayoutSettings.CurrentPreset)
                or nameof(DesktopBoxLayoutSettings.IsFileNameVisible))
        {
            ResizeDrawerCover(
                (DrawerCoverColumns * LayoutSettings.DrawerCoverCellWidth)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2),
                (DrawerCoverRows * LayoutSettings.DrawerCoverCellHeight)
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
