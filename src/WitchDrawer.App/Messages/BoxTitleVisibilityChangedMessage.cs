namespace WitchDrawer.App.Messages;

public sealed record BoxTitleVisibilityChangedMessage(
    Guid BoxId,
    bool IsVisible);
