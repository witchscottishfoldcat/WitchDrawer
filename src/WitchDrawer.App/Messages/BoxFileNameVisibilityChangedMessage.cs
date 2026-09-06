namespace WitchDrawer.App.Messages;

public sealed record BoxFileNameVisibilityChangedMessage(
    Guid BoxId,
    bool IsVisible);
