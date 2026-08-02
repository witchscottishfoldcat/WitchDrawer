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
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Name;

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
        _ = LoadDrawerSortModeAsync();
    }

    private async Task LoadPresetAsync()
    {
        var preset = await _drawerService.GetSettingAsync(GetLayoutPresetSettingKey(Id));
        LayoutSettings.ApplyPresetWithoutCallback(preset);
    }

    internal static string GetLayoutPresetSettingKey(Guid boxId) => $"BoxPreset_{boxId}";

    internal static string GetTitleVisibilitySettingKey(Guid boxId) =>
        $"BoxTitleVisible:{boxId:N}";

    internal static string GetLegacyDrawerTitleVisibilitySettingKey(Guid boxId) =>
        $"DrawerTitleVisible:{boxId:N}";

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"DrawerSortMode:{boxId:N}";

    public DesktopBoxLayoutSettings LayoutSettings { get; }
    
    public Box Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public BoxType Type => Model.Type;

    public bool IsTodoBox => Type == BoxType.Todo;

    public bool IsDrawerBox => Type == BoxType.Drawer;

    public bool IsTitleVisible => _isTitleVisible;

    public string TitleVisibilityToolTip => IsTitleVisible ? "隐藏桌面收纳盒名称" : "显示桌面收纳盒名称";

    public string TitleVisibilityAutomationName => IsTitleVisible ? "隐藏名称" : "显示名称";

    public DrawerItemSortMode DrawerItemSortMode => _drawerItemSortMode;

    public string DrawerSortModeLabel => DrawerItemSortMode switch
    {
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
    private async Task ApplyDrawerSortModeAsync(DrawerItemSortMode sortMode)
    {
        if (!IsDrawerBox || _drawerItemSortMode == sortMode)
        {
            return;
        }

        await _drawerService.SetSettingAsync(
            GetDrawerSortModeSettingKey(Id),
            sortMode.ToString());
        ApplyDrawerSortMode(sortMode);
        WeakReferenceMessenger.Default.Send(new DrawerSortModeChangedMessage(Id, sortMode));
    }

    internal async Task LoadDrawerSortModeAsync()
    {
        if (!IsDrawerBox)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetDrawerSortModeSettingKey(Id));
        ApplyDrawerSortMode(
            Enum.TryParse<DrawerItemSortMode>(saved, ignoreCase: true, out var sortMode)
                ? sortMode
                : DrawerItemSortMode.Name);
    }

    private void ApplyDrawerSortMode(DrawerItemSortMode sortMode)
    {
        if (!SetProperty(ref _drawerItemSortMode, sortMode, nameof(DrawerItemSortMode)))
        {
            return;
        }

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
}

