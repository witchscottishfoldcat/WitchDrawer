namespace WitchDrawer.Core.Models;

internal enum PendingFileOperationKind
{
    Import = 1,
    Move = 2,
    Remove = 3
}

internal sealed record PendingFileOperation(
    Guid Id,
    PendingFileOperationKind Kind,
    Guid ItemId,
    string SourcePath,
    string TargetPath,
    bool IsDirectory,
    DrawerItem? ResultItem,
    bool IsCompensating = false);
