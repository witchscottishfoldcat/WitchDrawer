namespace WitchDrawer.Core.Models;

public sealed record Box(
    Guid Id,
    string Name,
    BoxType Type,
    string? StoragePath,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

