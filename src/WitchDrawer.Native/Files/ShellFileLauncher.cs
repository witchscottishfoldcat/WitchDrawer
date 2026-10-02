using System.Diagnostics;
using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Files;

public sealed class ShellFileLauncher : IFileLauncher
{
    private readonly Action<string, CancellationToken> _open;

    public ShellFileLauncher() : this(Open) { }

    internal ShellFileLauncher(Action<string, CancellationToken> open) => _open = open;

    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shell extensions can require STA. Use an on-demand worker so slow path
        // checks and Shell activation never occupy the WPF dispatcher.
        var worker = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _open(path, cancellationToken);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "WitchDrawer Shell open" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static void Open(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Cannot open a missing file or directory.", path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}

