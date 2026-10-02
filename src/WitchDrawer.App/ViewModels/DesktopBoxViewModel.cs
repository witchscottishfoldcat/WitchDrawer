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

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxViewModel : ObservableObject
{
    // 弹窗 chrome 预留 = DesktopBoxWindow.xaml 中 DrawerSecondaryPopup 根 Border 的
    // BorderThickness (1px × 2) + 内部 ListBox 的 Margin (10px × 2) + ListBox 默认
    // (Aero2) 模板内 Border 的 Padding (1px × 2，该值硬编码在主题模板中，与
    // DesktopBoxLayoutSettings.GridViewportFixedChromeInset 已计入的同款 2px 一致)。
    // 改动 XAML 中任一数值时必须同步，否则内容区会比可视口大出几像素，最下列图标
    // 会被弹窗下边缘裁掉，且上下边距不对称。
    internal const double DrawerSecondaryPanelChrome = 24;
    private const double MaximumDrawerSecondaryPanelDimension = 320;
    private const double EdgeExpandThreshold = 14;
    internal const double VisibleHeaderRowHeight = 24;
    private const string MappingViewModeSettingPrefix = "MappingViewMode:";
    private const string MappingListWidthSettingPrefix = "MappingListWidth:";
    private const string TodoPanelSizeSettingPrefix = "TodoPanelSize:";
    private const string MappingListViewMode = "List";
    private const string MappingGridViewMode = "Grid";
    private const string DrawerCoverSizeSettingPrefix = "DrawerCoverSize:";
    private const string TitleVisibilitySettingPrefix = "BoxTitleVisible:";
    private const string LegacyDrawerTitleVisibilitySettingPrefix = "DrawerTitleVisible:";
    private const string FileNameVisibilitySettingPrefix = "BoxFileNameVisible:";
    private const string RollUpSettingPrefix = "BoxRolledUp:";
    private const string HoverRollUpEnabledSettingPrefix = "BoxHoverRollUpEnabled:";
    private const string DrawerSortModeSettingPrefix = "DrawerSortMode:";
    private const double DefaultDrawerCoverWidth = 180;
    private const double DefaultDrawerCoverHeight = 112;
    private const double MaximumDrawerCoverDimension = 720;
    // A visible header replaces the normal grid's 6 DIP hidden-header spacer.
    private const double DrawerTitleHeightCompensation = DesktopBoxLayoutSettings.HiddenGridContentInset;
    internal const double MinimumMappingListWidth = 180;
    internal const double MaximumMappingListWidth = 720;
    internal const double MinimumTodoPanelWidth = 220;
    internal const double MinimumTodoPanelHeight = 190;
    internal const double MaximumTodoPanelWidth = 960;
    internal const double MaximumTodoPanelHeight = 720;
    private const double DefaultTodoPanelWidth = 310;
    private const double DefaultTodoPanelHeight = 300;

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IShellChangeNotifier _shellChangeNotifier;
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
    private double _mappingListWidth;
    private bool _hasCustomMappingListWidth;
    private double _todoPanelWidth = DefaultTodoPanelWidth;
    private double _todoPanelHeight = DefaultTodoPanelHeight;
    private string _newTodoTitle = string.Empty;
    private readonly SemaphoreSlim _todoViewGate = new(1, 1);
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;
    private bool _isDrawerExpanded;
    private bool _isTitleVisible = true;
    private bool _isFileNameVisible;
    private bool _isRolledUp;
    private bool _isHoverRollUpEnabled;
    private double _drawerCoverWidth = DefaultDrawerCoverWidth;
    private double _drawerCoverHeight = DefaultDrawerCoverHeight;
    private double _drawerCoverPreviewWidth = double.NaN;
    private double _drawerCoverPreviewHeight = double.NaN;
    private int _drawerCoverPreviewColumns;
    private int _drawerCoverPreviewRows;
    private int _drawerCoverColumns = 3;
    private int _drawerCoverRows = 2;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Free;
    private BoxSizeModeState _sizeMode = BoxSizeModeState.Adaptive;
    private int _occupiedColumns = 1;
    private int _occupiedRows = 1;
    private double _autoHideContentOpacity = 1;
    private double _autoHideBoxOpacity = 1;
    private double _autoHideTitleOpacity = 1;
    private double _autoHideBorderOpacity = 1;

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IShellChangeNotifier shellChangeNotifier,
        IAppLogger logger,
        BoxVisualStyle visualStyle,
        DesktopBoxLayoutSettings? layoutSettings = null)
    {
        _box = box;
        _visualStyle = visualStyle;
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _shellChangeNotifier = shellChangeNotifier;
        _logger = logger;
        _layoutSettings = layoutSettings ?? new DesktopBoxLayoutSettings(box.Type == BoxType.Drawer);
        _mappingListWidth = _layoutSettings.MappingListWidth;
        _layoutSettings.PropertyChanged += OnLayoutSettingsChanged;

        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        UseMappingGridModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: false));
        UseMappingListModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: true));
        AddTodoCommand = new AsyncRelayCommand(AddTodoAsync, CanAddTodo);
        ToggleTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(ToggleTodoAsync, CanMutateTodo);
        ArchiveCompletedTodosCommand = new AsyncRelayCommand(ArchiveCompletedTodosAsync, CanArchiveCompletedTodos);
        DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(DeleteTodoAsync, CanMutateTodo);
        SaveTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(SaveTodoAsync, CanMutateTodo);
        UndoDeleteCommand = new AsyncRelayCommand(UndoDeleteAsync, () => !IsBusy && Undo.IsAvailable);
        Undo.AvailabilityChanged += (_, _) => UndoDeleteCommand.NotifyCanExecuteChanged();
        UpdateGridCanvasSize();
    }

    public DesktopBoxLayoutSettings LayoutSettings => _layoutSettings;

    public double MappingListWidth => _mappingListWidth;

    public double TodoPanelWidth => _todoPanelWidth;

    public double TodoPanelHeight => _todoPanelHeight;

    /// <summary>
    /// 自动隐藏开启且未悬停时，收纳盒内容的可见度（0..1），默认完全可见。
    /// 与桌面盒子透明度（背景外观）相互独立。
    /// </summary>
    public double AutoHideContentOpacity
    {
        get => _autoHideContentOpacity;
        private set => SetProperty(ref _autoHideContentOpacity, value);
    }

    /// <summary>
    /// 自动隐藏开启且未悬停时，收纳盒外壳（背景/边框/阴影）的可见度（0..1）。
    /// 仅当勾选“收纳盒”参与透明时为隐藏态透明度，否则为 1。与内容透明相互独立、不叠加。
    /// </summary>
    public double AutoHideBoxOpacity
    {
        get => _autoHideBoxOpacity;
        private set => SetProperty(ref _autoHideBoxOpacity, value);
    }

    /// <summary>
    /// 自动隐藏开启且未悬停时，收纳盒标题的可见度（0..1）。
    /// 仅当勾选“收纳盒标题”参与透明时为隐藏态透明度，否则为 1。
    /// </summary>
    public double AutoHideTitleOpacity
    {
        get => _autoHideTitleOpacity;
        private set => SetProperty(ref _autoHideTitleOpacity, value);
    }

    /// <summary>
    /// 自动隐藏开启且未悬停时，收纳盒边框（描边）的可见度（0..1）。
    /// 仅当勾选“收纳盒边框”参与透明时为隐藏态透明度，否则为 1。与内容透明相互独立、不叠加。
    /// </summary>
    public double AutoHideBorderOpacity
    {
        get => _autoHideBorderOpacity;
        private set => SetProperty(ref _autoHideBorderOpacity, value);
    }

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

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public int GridLayoutVersion { get; private set; }

    public ResettableObservableCollection<DrawerItemViewModel> DrawerPreviewItems { get; } = [];

    public ResettableObservableCollection<DrawerCoverTileViewModel> DrawerCoverTiles { get; } = [];

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

    public IAsyncRelayCommand<TodoItemViewModel?> SaveTodoCommand { get; }
    public IAsyncRelayCommand UndoDeleteCommand { get; }
    public TodoUndoViewModel Undo { get; } = new();
    public bool HasTodos => TodoItems.Count > 0;

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

    public bool IsHoverRollUpEnabled => SupportsRollUp && _isHoverRollUpEnabled;

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

    public double DrawerCoverDisplayWidth => double.IsNaN(_drawerCoverPreviewWidth)
        ? DrawerCoverWidth
        : _drawerCoverPreviewWidth;

    public double DrawerCoverHeight => _drawerCoverHeight;

    public double DrawerContentHeight => CalculateDrawerContentHeight(
        DrawerCoverHeight,
        IsTitleVisible);

    public double DrawerCoverDisplayContentHeight => CalculateDrawerContentHeight(
        double.IsNaN(_drawerCoverPreviewHeight) ? DrawerCoverHeight : _drawerCoverPreviewHeight,
        IsTitleVisible);

    public int DrawerCoverColumns => _drawerCoverColumns;

    public int DrawerCoverRows => _drawerCoverRows;

    public double DrawerCoverGridWidth => DrawerCoverColumns * LayoutSettings.DrawerCoverCellWidth;

    public double DrawerCoverGridHeight => DrawerCoverRows * LayoutSettings.DrawerCoverCellHeight;

    public int DrawerCoverDisplayColumns => double.IsNaN(_drawerCoverPreviewWidth)
        ? DrawerCoverColumns
        : _drawerCoverPreviewColumns;

    public int DrawerCoverDisplayRows => double.IsNaN(_drawerCoverPreviewHeight)
        ? DrawerCoverRows
        : _drawerCoverPreviewRows;

    public double DrawerCoverDisplayGridWidth =>
        DrawerCoverDisplayColumns * LayoutSettings.DrawerCoverCellWidth;

    public double DrawerCoverDisplayGridHeight =>
        DrawerCoverDisplayRows * LayoutSettings.DrawerCoverCellHeight;

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

    // The grid already has at least two columns and one row. Independent
    // minimum dimensions add unequal whitespace to small (especially 2x2)
    // menus; use the same chrome around the actual grid on both axes.
    public double DrawerSecondaryPanelWidth => Math.Min(
        (DrawerSecondaryColumns
            * LayoutSettings.ItemSlotWidth)
        + DrawerSecondaryPanelChrome,
        MaximumDrawerSecondaryPanelDimension);

    public double DrawerSecondaryPanelHeight => Math.Min(
        (Math.Min(5, DrawerSecondaryRows)
            * LayoutSettings.ItemSlotHeight)
        + DrawerSecondaryPanelChrome,
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
            : DesktopBoxLayoutSettings.HiddenGridContentInset;
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
        foreach (var item in Items)
            item.UpdateBoxPresentation(Name, IsPixelStyle, GetIconPixelSize(IsPixelStyle));
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
        OnPropertyChanged(nameof(DrawerCoverDisplayContentHeight));
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

    /// <summary>
    /// 固定模式下的总格数；自适应模式视为无限。
    /// </summary>
    public int FixedCapacity => IsFixedSize ? _sizeMode.Columns * _sizeMode.Rows : int.MaxValue;

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

    private sealed record DrawerSortEntry(
        DrawerItemViewModel Item,
        string Name,
        string ItemType,
        long Size,
        DateTime ModifiedDateUtc);

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
