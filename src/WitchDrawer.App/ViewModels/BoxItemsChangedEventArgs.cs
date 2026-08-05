namespace WitchDrawer.App.ViewModels;

public sealed class BoxItemsChangedEventArgs(Guid boxId) : EventArgs
{
    public Guid BoxId { get; } = boxId;
}
