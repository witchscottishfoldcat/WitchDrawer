namespace WitchDrawer.Core.Abstractions;

public interface IFileLauncher
{
    Task OpenAsync(string path, CancellationToken cancellationToken = default);
}

