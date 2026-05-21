namespace WitchDrawer.Core.Models;

public sealed record DrawerItem(
    Guid Id,
    Guid BoxId,
    string DisplayName,
    ItemKind ItemKind,
    string? SourcePath,
    string? StoredPath,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string? EffectivePath => StoredPath ?? SourcePath;
}

