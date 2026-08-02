namespace WitchDrawer.App.ViewModels;

public sealed class DrawerCoverTileViewModel
{
    private DrawerCoverTileViewModel(DrawerItemViewModel? item, bool isExpandTile)
    {
        Item = item;
        IsExpandTile = isExpandTile;
    }

    public DrawerItemViewModel? Item { get; }

    public bool HasItem => Item is not null;

    public bool IsExpandTile { get; }

    public static DrawerCoverTileViewModel ForItem(DrawerItemViewModel? item) =>
        new(item, isExpandTile: false);

    public static DrawerCoverTileViewModel Expand() =>
        new(item: null, isExpandTile: true);
}
