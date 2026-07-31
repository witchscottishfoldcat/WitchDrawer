namespace WitchDrawer.App.Messages;

public sealed record BoxPositionLockStateChangedMessage(
    Guid BoxId,
    bool IsPositionLocked);
