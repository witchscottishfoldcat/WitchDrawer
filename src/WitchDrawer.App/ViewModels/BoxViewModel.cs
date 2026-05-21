using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class BoxViewModel
{
    public BoxViewModel(Box model)
    {
        Model = model;
    }

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

