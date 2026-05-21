namespace WitchDrawer.Core.Abstractions;

public interface IFileTrash
{
    Task MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default);
}

