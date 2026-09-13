namespace WitchDrawer.Core.Models;

public sealed record TodoDeleteUndo(Guid Token, Guid BoxId, DateTimeOffset ExpiresAt);
