using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class BoxViewModel : ObservableObject
{
    private readonly DrawerService _drawerService;
    private readonly IAppLogger _logger;
    private BoxVisualStyle _visualStyle;
    private bool _isPositionLocked;
    private bool _isTitleVisible = true;
    private bool _isFileNameVisible;
    private bool _isHoverRollUpEnabled;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Free;

    public BoxViewModel(
        Box model,
        DrawerService drawerService,
        BoxVisualStyle visualStyle,
        bool isPositionLocked,
        IAppLogger? logger = null)
    {
        Model = model;
        _drawerService = drawerService;
        _logger = logger ?? NullAppLogger.Instance;
        _visualStyle = visualStyle;
        _isPositionLocked = isPositionLocked;

        LayoutSettings = new DesktopBoxLayoutSettings(model.Type == BoxType.Drawer);
        LayoutSettings.SetPresetChangedCallback(async (preset) => 
        {
            await _drawerService.SetSettingAsync(GetLayoutPresetSettingKey(Id), preset);
            WeakReferenceMessenger.Default.Send(new BoxLayoutPresetChangedMessage(Id, preset));
        });

        WeakReferenceMessenger.Default.Register<BoxViewModel, BoxLayoutPresetChangedMessage>(
            this,
            static (recipient, message) =>
            {
                if (recipient.Id == message.BoxId)
                {
                    recipient.LayoutSettings.ApplyPresetWithoutCallback(message.Preset);
                }
            });

    }

    /// <summary>
    /// 明确、可等待的设置初始化。启动路径传入快照避免逐项打开数据库连接；
    /// 运行期间新建/替换视图模型时传 null，实时读取数据库以保证不拿到过期快照。
    /// 单项加载失败保留默认值并记录日志（与原 FireAndForget 行为一致）。
    /// </summary>
    internal async Task InitializeSettingsAsync(StartupSettingsSnapshot? snapshot = null)
    {
        await TryLoadAsync(() => LoadPresetAsync(snapshot), "layout preset");
        await TryLoadAsync(() => LoadTitleVisibilityAsync(snapshot), "title visibility");
        await TryLoadAsync(() => LoadFileNameVisibilityAsync(snapshot), "file name visibility");
        await TryLoadAsync(() => LoadHoverRollUpEnabledAsync(snapshot), "hover roll-up setting");
        await TryLoadAsync(() => LoadDrawerSortModeAsync(snapshot), "drawer sort mode");
    }

    private async Task TryLoadAsync(Func<Task> load, string description)
    {
        try
        {
            await load();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Failed to load {description} for box {Id:N}.");
        }
    }

    private async Task<string?> ReadSettingAsync(string key, StartupSettingsSnapshot? snapshot)
        => snapshot is not null
            ? snapshot.Get(key)
            : await _drawerService.GetSettingAsync(key);

    private async Task LoadPresetAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var preset = await ReadSettingAsync(GetLayoutPresetSettingKey(Id), snapshot);
        LayoutSettings.ApplyPresetWithoutCallback(preset);
    }

    internal static string GetLayoutPresetSettingKey(Guid boxId) => $"BoxPreset_{boxId}";

    internal static string GetSizeModeSettingKey(Guid boxId) => $"BoxSizeMode:{boxId:N}";

    internal static string GetTitleVisibilitySettingKey(Guid boxId) =>
        $"BoxTitleVisible:{boxId:N}";

    internal static string GetLegacyDrawerTitleVisibilitySettingKey(Guid boxId) =>
        $"DrawerTitleVisible:{boxId:N}";

    internal static string GetFileNameVisibilitySettingKey(Guid boxId) =>
        $"BoxFileNameVisible:{boxId:N}";

    internal static string GetHoverRollUpEnabledSettingKey(Guid boxId) =>
        $"BoxHoverRollUpEnabled:{boxId:N}";

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"DrawerSortMode:{boxId:N}";

    /// <summary>
    /// 统一排序设置的 key（所有收纳盒型共用）。读取时抽屉盒会回退迁移
    /// <see cref="GetDrawerSortModeSettingKey"/> 的旧值。
    /// </summary>
    internal static string GetBoxSortModeSettingKey(Guid boxId) =>
        $"BoxSortMode:{boxId:N}";

    public DesktopBoxLayoutSettings LayoutSettings { get; }
    
    public Box Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public BoxType Type => Model.Type;

    public bool IsTodoBox => Type == BoxType.Todo;

    public bool IsDrawerBox => Type == BoxType.Drawer;

    /// <summary>
    /// 固定 m×n 格尺寸仅适用于普通网格收纳盒；其余盒型始终自适应。
    /// </summary>
    public bool SupportsFixedSize => Type is BoxType.Normal or BoxType.Pixel;

    /// <summary>
    /// 排序（自由/名称/大小/类型/修改日期）适用于所有收纳类盒型；待办盒有自己的排序语义。
    /// </summary>
    public bool SupportsSorting => Type is BoxType.Normal or BoxType.Pixel or BoxType.Mapping or BoxType.Drawer;

    public bool IsTitleVisible => _isTitleVisible;

    public string TitleVisibilityToolTip => IsTitleVisible ? "隐藏桌面收纳盒名称" : "显示桌面收纳盒名称";

    public string TitleVisibilityAutomationName => IsTitleVisible ? "隐藏名称" : "显示名称";

    public bool SupportsFileNameVisibility =>
        Type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer;

    public bool IsFileNameVisible => _isFileNameVisible;

    public string FileNameVisibilityAutomationName =>
        IsFileNameVisible ? "隐藏文件名" : "显示文件名";

    public bool SupportsHoverRollUp =>
        Type is BoxType.Normal or BoxType.Pixel or BoxType.Mapping;

    public bool IsHoverRollUpEnabled => SupportsHoverRollUp && _isHoverRollUpEnabled;

    public string HoverRollUpButtonLabel => IsHoverRollUpEnabled ? "已开启" : "已关闭";

    public string HoverRollUpButtonToolTip => IsHoverRollUpEnabled
        ? "关闭鼠标悬停标题自动展开"
        : "开启鼠标悬停标题自动展开";

    public DrawerItemSortMode DrawerItemSortMode => _drawerItemSortMode;

    public bool IsFreeSort => DrawerItemSortMode == DrawerItemSortMode.Free;

    public string DrawerSortModeLabel => DrawerItemSortMode switch
    {
        DrawerItemSortMode.Free => "自由",
        DrawerItemSortMode.Size => "大小",
        DrawerItemSortMode.ItemType => "项目类型",
        DrawerItemSortMode.ModifiedDate => "修改日期",
        _ => "名称"
    };

    public bool IsDrawerSortByName => DrawerItemSortMode == DrawerItemSortMode.Name;

    public bool IsDrawerSortBySize => DrawerItemSortMode == DrawerItemSortMode.Size;

    public bool IsDrawerSortByItemType => DrawerItemSortMode == DrawerItemSortMode.ItemType;

    public bool IsDrawerSortByModifiedDate => DrawerItemSortMode == DrawerItemSortMode.ModifiedDate;

    public BoxVisualStyle VisualStyle => _visualStyle;

    public bool IsPixelStyle => VisualStyle == BoxVisualStyle.Pixel;

    public bool CanSelectVisualStyle => Type is BoxType.Normal or BoxType.Pixel;

    public bool IsPositionLocked => _isPositionLocked;

    public string PositionLockButtonToolTip =>
        IsPositionLocked ? "解锁桌面位置" : "锁定桌面位置";

    public string PositionLockButtonAutomationName =>
        IsPositionLocked ? "解锁当前收纳盒桌面位置" : "锁定当前收纳盒桌面位置";

    public string VisualStyleLabel => BoxVisualStyleCatalog.GetOption(VisualStyle).Name;

    public string TypeLabel => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "普通",
        BoxType.Mapping => "映射",
        BoxType.Todo => "待办",
        BoxType.Drawer => "抽屉",
        _ => "未知"
    };

    public string Description => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "拖入后移动到收纳盒",
        BoxType.Mapping => "只保存路径引用",
        BoxType.Todo => "独立桌面待办清单",
        BoxType.Drawer => "安卓式展开抽屉",
        _ => string.Empty
    };

    public string Badge => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "N",
        BoxType.Mapping => "M",
        BoxType.Todo => "T",
        BoxType.Drawer => "D",
        _ => "?"
    };

    public string StorageLabel => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel or BoxType.Drawer => Model.StoragePath ?? string.Empty,
        BoxType.Todo => "待办事项保存在本地数据库",
        _ => "源文件保留在原位置"
    };

    public string DeleteWarning => Model.Type switch
    {
        BoxType.Todo => "该待办盒中的所有事项（包括归档历史）将一并删除，此操作无法撤销。",
        BoxType.Mapping => "只会移除映射引用，源文件不会被移动或删除。",
        _ => "收纳盒内的文件将恢复到原来的位置；如有重名会自动加后缀。"
    };

    public void ApplyVisualStyle(BoxVisualStyle visualStyle)
    {
        if (_visualStyle == visualStyle)
        {
            return;
        }

        _visualStyle = visualStyle;
        OnPropertyChanged(nameof(VisualStyle));
        OnPropertyChanged(nameof(IsPixelStyle));
        OnPropertyChanged(nameof(VisualStyleLabel));
    }

    public void ApplyPositionLockState(bool isPositionLocked)
    {
        if (!SetProperty(
                ref _isPositionLocked,
                isPositionLocked,
                nameof(IsPositionLocked)))
        {
            return;
        }

        OnPropertyChanged(nameof(PositionLockButtonToolTip));
        OnPropertyChanged(nameof(PositionLockButtonAutomationName));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ToggleTitleVisibilityAsync()
    {
        var isVisible = !IsTitleVisible;
        await _drawerService.SetSettingAsync(
            GetTitleVisibilitySettingKey(Id),
            isVisible.ToString());
        ApplyTitleVisibility(isVisible);
        WeakReferenceMessenger.Default.Send(
            new BoxTitleVisibilityChangedMessage(Id, isVisible));
    }

    internal async Task LoadTitleVisibilityAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(GetTitleVisibilitySettingKey(Id), snapshot);
        if (saved is null && IsDrawerBox)
        {
            saved = await ReadSettingAsync(
                GetLegacyDrawerTitleVisibilitySettingKey(Id), snapshot);
        }

        ApplyTitleVisibility(!bool.TryParse(saved, out var isVisible) || isVisible);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ToggleFileNameVisibilityAsync()
    {
        if (!SupportsFileNameVisibility)
        {
            return;
        }

        var isVisible = !IsFileNameVisible;
        await _drawerService.SetSettingAsync(
            GetFileNameVisibilitySettingKey(Id),
            isVisible.ToString());
        ApplyFileNameVisibility(isVisible);
        WeakReferenceMessenger.Default.Send(
            new BoxFileNameVisibilityChangedMessage(Id, isVisible));
    }

    internal async Task LoadFileNameVisibilityAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(GetFileNameVisibilitySettingKey(Id), snapshot);
        ApplyFileNameVisibility(bool.TryParse(saved, out var isVisible) && isVisible);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ToggleHoverRollUpAsync()
    {
        if (!SupportsHoverRollUp)
        {
            return;
        }

        var isEnabled = !IsHoverRollUpEnabled;
        await _drawerService.SetSettingAsync(
            GetHoverRollUpEnabledSettingKey(Id),
            isEnabled.ToString());
        ApplyHoverRollUpEnabled(isEnabled);
        WeakReferenceMessenger.Default.Send(
            new BoxHoverRollUpEnabledChangedMessage(Id, isEnabled));
    }

    internal async Task LoadHoverRollUpEnabledAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(
            GetHoverRollUpEnabledSettingKey(Id), snapshot);
        ApplyHoverRollUpEnabled(bool.TryParse(saved, out var isEnabled) && isEnabled);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ApplyDrawerSortModeAsync(DrawerItemSortMode sortMode)
    {
        if (!SupportsSorting || _drawerItemSortMode == sortMode)
        {
            return;
        }

        await _drawerService.SetSettingAsync(
            GetBoxSortModeSettingKey(Id),
            sortMode.ToString());
        ApplyDrawerSortMode(sortMode);
        WeakReferenceMessenger.Default.Send(new DrawerSortModeChangedMessage(Id, sortMode));
    }

    internal async Task LoadDrawerSortModeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!SupportsSorting)
        {
            return;
        }

        var saved = await ReadSettingAsync(GetBoxSortModeSettingKey(Id), snapshot);
        if (saved is null && IsDrawerBox)
        {
            // 迁移抽屉盒旧的 DrawerSortMode: 设置值。
            saved = await ReadSettingAsync(GetDrawerSortModeSettingKey(Id), snapshot);
        }

        ApplyDrawerSortMode(
            Enum.TryParse<DrawerItemSortMode>(saved, ignoreCase: true, out var sortMode)
                ? sortMode
                : DrawerItemSortMode.Free);
    }

    private void ApplyDrawerSortMode(DrawerItemSortMode sortMode)
    {
        if (!SetProperty(ref _drawerItemSortMode, sortMode, nameof(DrawerItemSortMode)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsFreeSort));
        OnPropertyChanged(nameof(IsDrawerSortByName));
        OnPropertyChanged(nameof(IsDrawerSortBySize));
        OnPropertyChanged(nameof(IsDrawerSortByItemType));
        OnPropertyChanged(nameof(IsDrawerSortByModifiedDate));
        OnPropertyChanged(nameof(DrawerSortModeLabel));
    }

    private void ApplyTitleVisibility(bool isVisible)
    {
        if (!SetProperty(
                ref _isTitleVisible,
                isVisible,
                nameof(IsTitleVisible)))
        {
            return;
        }

        OnPropertyChanged(nameof(TitleVisibilityToolTip));
        OnPropertyChanged(nameof(TitleVisibilityAutomationName));
    }

    private void ApplyFileNameVisibility(bool isVisible)
    {
        LayoutSettings.IsFileNameVisible = isVisible;
        if (!SetProperty(
                ref _isFileNameVisible,
                isVisible,
                nameof(IsFileNameVisible)))
        {
            return;
        }

        OnPropertyChanged(nameof(FileNameVisibilityAutomationName));
    }

    private void ApplyHoverRollUpEnabled(bool isEnabled)
    {
        if (!SetProperty(
                ref _isHoverRollUpEnabled,
                SupportsHoverRollUp && isEnabled,
                nameof(IsHoverRollUpEnabled)))
        {
            return;
        }

        OnPropertyChanged(nameof(HoverRollUpButtonLabel));
        OnPropertyChanged(nameof(HoverRollUpButtonToolTip));
    }
}

