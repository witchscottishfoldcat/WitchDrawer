using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Messages;

public sealed record DrawerSortModeChangedMessage(
    Guid BoxId,
    DrawerItemSortMode SortMode);
