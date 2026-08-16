using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class BoxViewModel : ObservableObject
{
    private readonly DrawerService _drawerService;
    private BoxVisualStyle _visualStyle;
    private bool _isPositionLocked;
    private bool _isTitleVisible = true;
    private bool _isFileNameVisible;
    private bool _isDetailExpandEnabled;
    private bool _isDetailOpenSingle = true;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Free;

    public BoxViewModel(
        Box model,
        DrawerService drawerService,
        BoxVisualStyle visualStyle,
        bool isPositionLocked)
    {
        Model = model;
        _drawerService = drawerService;
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

        _ = LoadPresetAsync();
        _ = LoadTitleVisibilityAsync();
        _ = LoadFileNameVisibilityAsync();
        _ = LoadDrawerSortModeAsync();
        _ = LoadDetailExpandAsync();
        _ = LoadDetailOpenModeAsync();
    }

    private async Task LoadPresetAsync()
    {
        var preset = await _drawerService.GetSettingAsync(GetLayoutPresetSettingKey(Id));
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

    internal static string GetDetailExpandSettingKey(Guid boxId) =>
        $"BoxDetailExpand:{boxId:N}";

    /// <summary>详细视图打开方式（单击/双击展开）设置键，仅映射盒有意义。</summary>
    internal static string GetDetailOpenModeSettingKey(Guid boxId) =>
        $"BoxDetailOpenMode:{boxId:N}";

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

    public bool IsMappingBox => Type == BoxType.Mapping;

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
        Type is BoxType.Normal or BoxType.Pixel;

    public bool IsFileNameVisible => _isFileNameVisible;

    public string FileNameVisibilityAutomationName =>
        IsFileNameVisible ? "隐藏文件名" : "显示文件名";

    /// <summary>
    /// 映射盒「详细功能」开关（按盒持久化）。开启后获得单击空白两级展开预览与拖拽交换能力。
    /// </summary>
    public bool IsDetailExpandEnabled => _isDetailExpandEnabled;

    public string DetailExpandAutomationName =>
        IsDetailExpandEnabled ? "关闭详细功能" : "开启详细功能";

    /// <summary>详细视图打开方式：true=单击空白展开（默认），false=双击空白展开。按盒持久化。</summary>
    public bool IsDetailOpenSingle => _isDetailOpenSingle;

    /// <summary>打开方式短标签（用于开关卡片内单选按钮）。</summary>
    public string DetailOpenModeLabel => _isDetailOpenSingle ? "单击" : "双击";

    public bool IsDetailOpenDoubleClick => !_isDetailOpenSingle;

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
        BoxType.Todo => "该待办盒中的所有事项将一并删除，此操作无法撤销。",
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

    internal async Task LoadTitleVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetTitleVisibilitySettingKey(Id));
        if (saved is null && IsDrawerBox)
        {
            saved = await _drawerService.GetSettingAsync(
                GetLegacyDrawerTitleVisibilitySettingKey(Id));
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

    internal async Task LoadFileNameVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetFileNameVisibilitySettingKey(Id));
        ApplyFileNameVisibility(bool.TryParse(saved, out var isVisible) && isVisible);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(AllowConcurrentExecutions = false)]
    private Task ToggleDetailExpandAsync() =>
        ToggleDetailExpandCoreAsync((key, value) => _drawerService.SetSettingAsync(key, value));

    /// <summary>
    /// 带持久化委托重载：供单元测试注入可控时序的写盘，确定性验证命令并发重入（审查 Q1）。
    /// 命令级已禁用并发执行，执行中再次触发会被忽略。
    /// </summary>
    internal async Task ToggleDetailExpandCoreAsync(Func<string, string, Task> persistSetting)
    {
        if (!IsMappingBox)
        {
            return;
        }

        var isEnabled = !IsDetailExpandEnabled;
        try
        {
            await persistSetting(GetDetailExpandSettingKey(Id), isEnabled.ToString());
        }
        catch
        {
            // 持久化失败时不更新 UI 状态，避免开关显示与磁盘状态不一致。
            return;
        }

        ApplyDetailExpand(isEnabled);
        WeakReferenceMessenger.Default.Send(
            new BoxDetailExpandChangedMessage(Id, isEnabled));
    }

    internal Task LoadDetailExpandAsync() =>
        LoadDetailExpandAsync(() => _drawerService.GetSettingAsync(GetDetailExpandSettingKey(Id)));

    /// <summary>
    /// 带读取委托重载：供单元测试注入可控时序的读取，确定性验证加载竞态。
    /// 加载期间若用户已切换开关（最后写入者胜），丢弃过期加载结果，
    /// 避免异步读回写旧值覆盖用户操作（审查 F2）。
    /// </summary>
    internal async Task LoadDetailExpandAsync(Func<Task<string?>> readSetting)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var snapshot = _isDetailExpandEnabled;
            var saved = await readSetting();
            if (_isDetailExpandEnabled != snapshot)
            {
                // 加载期间用户切换过开关，读到的旧值已过期，丢弃。
                return;
            }

            ApplyDetailExpand(bool.TryParse(saved, out var isEnabled) && isEnabled);
        }
        catch
        {
            // 读取失败时保持默认关闭，避免 fire-and-forget 加载路径产生未观察异常。
        }
    }

    private void ApplyDetailExpand(bool isEnabled)
    {
        if (!SetProperty(
                ref _isDetailExpandEnabled,
                isEnabled,
                nameof(IsDetailExpandEnabled)))
        {
            return;
        }

        OnPropertyChanged(nameof(DetailExpandAutomationName));
    }

    /// <summary>
    /// 设置详细视图打开方式（"Single"=单击空白展开 / "Double"=双击空白展开）。
    /// 持久化失败时不更新 UI 状态，避免开关显示与磁盘状态不一致。
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand(AllowConcurrentExecutions = false)]
    private Task SetDetailOpenModeAsync(string? mode) =>
        SetDetailOpenModeCoreAsync(mode, (key, value) => _drawerService.SetSettingAsync(key, value));

    /// <summary>
    /// 带持久化委托重载：供单元测试注入可控时序的写盘，确定性验证命令并发重入（审查 Q1）。
    /// 命令级已禁用并发执行，执行中再次触发会被忽略。
    /// </summary>
    internal async Task SetDetailOpenModeCoreAsync(
        string? mode,
        Func<string, string, Task> persistSetting)
    {
        if (!IsMappingBox)
        {
            return;
        }

        var isSingleClick = !string.Equals(mode, "Double", StringComparison.OrdinalIgnoreCase);
        if (isSingleClick == _isDetailOpenSingle)
        {
            return;
        }

        try
        {
            await persistSetting(GetDetailOpenModeSettingKey(Id), isSingleClick ? "Single" : "Double");
        }
        catch
        {
            // 持久化失败时不更新 UI 状态，避免开关显示与磁盘状态不一致。
            return;
        }

        ApplyDetailOpenMode(isSingleClick);
        WeakReferenceMessenger.Default.Send(
            new BoxDetailOpenModeChangedMessage(Id, isSingleClick));
    }

    internal Task LoadDetailOpenModeAsync() =>
        LoadDetailOpenModeAsync(() => _drawerService.GetSettingAsync(GetDetailOpenModeSettingKey(Id)));

    /// <summary>
    /// 带读取委托重载：供单元测试注入可控时序的读取，确定性验证加载竞态。
    /// 加载期间若用户已切换打开方式（最后写入者胜），丢弃过期加载结果，
    /// 避免异步读回写旧值覆盖用户操作（审查 F2）。
    /// </summary>
    internal async Task LoadDetailOpenModeAsync(Func<Task<string?>> readSetting)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var snapshot = _isDetailOpenSingle;
            var saved = await readSetting();
            if (_isDetailOpenSingle != snapshot)
            {
                // 加载期间用户切换过打开方式，读到的旧值已过期，丢弃。
                return;
            }

            ApplyDetailOpenMode(!string.Equals(saved, "Double", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // 读取失败时保持默认单击展开，避免 fire-and-forget 加载路径产生未观察异常。
        }
    }

    private void ApplyDetailOpenMode(bool isSingleClick)
    {
        if (!SetProperty(
                ref _isDetailOpenSingle,
                isSingleClick,
                nameof(IsDetailOpenSingle)))
        {
            return;
        }

        OnPropertyChanged(nameof(DetailOpenModeLabel));
        OnPropertyChanged(nameof(IsDetailOpenDoubleClick));
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

    internal async Task LoadDrawerSortModeAsync()
    {
        if (!SupportsSorting)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetBoxSortModeSettingKey(Id));
        if (saved is null && IsDrawerBox)
        {
            // 迁移抽屉盒旧的 DrawerSortMode: 设置值。
            saved = await _drawerService.GetSettingAsync(GetDrawerSortModeSettingKey(Id));
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
}

