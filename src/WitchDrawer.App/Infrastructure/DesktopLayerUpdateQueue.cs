using System.Windows.Threading;

namespace WitchDrawer.App.Infrastructure;

/// <summary>Coalesces event bursts on the UI dispatcher; never schedules a timer.</summary>
internal sealed class DesktopLayerUpdateQueue(
    Dispatcher dispatcher, Action update, Action<Exception> onError) : IDisposable
{
    private int _queued;
    private int _disposed;
    private bool _updating;

    public void Request()
    {
        if (Volatile.Read(ref _disposed) != 0 || dispatcher.HasShutdownStarted
            || (dispatcher.CheckAccess() && _updating)
            || Interlocked.Exchange(ref _queued, 1) != 0)
        {
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Render, Drain);
        }
        catch (InvalidOperationException exception)
        {
            Volatile.Write(ref _queued, 0);
            if (!dispatcher.HasShutdownStarted && Volatile.Read(ref _disposed) == 0)
            {
                onError(exception);
            }
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _queued, 0);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _updating = true;
        try
        {
            update();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
