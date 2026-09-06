using CommunityToolkit.Mvvm.ComponentModel;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DrawerCoverTileViewModel : ObservableObject
{
    private DrawerCoverTileViewModel(DrawerItemViewModel? item, bool isExpandTile)
    {
        Item = item;
        IsExpandTile = isExpandTile;
    }

    public DrawerItemViewModel? Item { get; }

    public bool HasItem => Item is not null;

    public bool IsExpandTile { get; }

    /// <summary>
    /// 封面磁贴选中态：单击只选中（蓝框），双击才打开——与图标网格的交互一致，
    /// 避免误触直接启动程序。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    public static DrawerCoverTileViewModel ForItem(DrawerItemViewModel? item) =>
        new(item, isExpandTile: false);

    public static DrawerCoverTileViewModel Expand() =>
        new(item: null, isExpandTile: true);
}
