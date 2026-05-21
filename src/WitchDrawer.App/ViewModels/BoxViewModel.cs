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

    public string TypeLabel => Model.Type == BoxType.Normal ? "普通" : "映射";

    public string Description => Model.Type == BoxType.Normal ? "拖入后移动到收纳盒" : "只保存路径引用";

    public string Badge => Model.Type == BoxType.Normal ? "N" : "M";

    public string StorageLabel => Model.Type == BoxType.Normal ? Model.StoragePath ?? string.Empty : "源文件保留在原位置";
}

