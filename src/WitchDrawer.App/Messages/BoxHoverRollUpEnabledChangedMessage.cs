namespace WitchDrawer.App.Messages;

public sealed record BoxHoverRollUpEnabledChangedMessage(
    Guid BoxId,
    bool IsEnabled);
