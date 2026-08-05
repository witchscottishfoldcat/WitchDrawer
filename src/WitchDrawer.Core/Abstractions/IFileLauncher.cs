namespace WitchDrawer.Core.Abstractions;

public interface IFileLauncher
{
    Task OpenAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the file with elevation (the shell "runas" verb).
    /// </summary>
    Task OpenAsAdminAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the containing folder in Explorer with the item selected.
    /// </summary>
    Task ShowInFolderAsync(string path, CancellationToken cancellationToken = default);
}
