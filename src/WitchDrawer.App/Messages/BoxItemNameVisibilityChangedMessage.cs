namespace WitchDrawer.App.Messages;

public sealed record BoxItemNameVisibilityChangedMessage(Guid BoxId, bool IsVisible);
