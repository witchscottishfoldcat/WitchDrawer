using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class BoxViewModel
{
    private readonly DrawerService _drawerService;

    public BoxViewModel(Box model, DrawerService drawerService)
    {
        Model = model;
        _drawerService = drawerService;

        LayoutSettings = new DesktopBoxLayoutSettings();
        LayoutSettings.SetPresetChangedCallback(async (preset) => 
        {
            await _drawerService.SetSettingAsync($"BoxPreset_{Id}", preset);
            WeakReferenceMessenger.Default.Send(new BoxLayoutPresetChangedMessage(Id, preset));
        });

        _ = LoadPresetAsync();
    }

    private async Task LoadPresetAsync()
    {
        var preset = await _drawerService.GetSettingAsync($"BoxPreset_{Id}");
        if (!string.IsNullOrEmpty(preset))
        {
            LayoutSettings.ApplyPresetCommand.Execute(preset);
        }
    }

    public DesktopBoxLayoutSettings LayoutSettings { get; }
    
    public Box Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public BoxType Type => Model.Type;

    public string TypeLabel => Model.Type switch
    {
        BoxType.Normal => "普通",
        BoxType.Mapping => "映射",
        BoxType.Pixel => "像素",
        _ => "未知"
    };

    public string Description => Model.Type switch
    {
        BoxType.Normal => "拖入后移动到收纳盒",
        BoxType.Mapping => "只保存路径引用",
        BoxType.Pixel => "像素艺术风格收纳",
        _ => string.Empty
    };

    public string Badge => Model.Type switch
    {
        BoxType.Normal => "N",
        BoxType.Mapping => "M",
        BoxType.Pixel => "P",
        _ => "?"
    };

    public string StorageLabel => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel => Model.StoragePath ?? string.Empty,
        _ => "源文件保留在原位置"
    };
}

