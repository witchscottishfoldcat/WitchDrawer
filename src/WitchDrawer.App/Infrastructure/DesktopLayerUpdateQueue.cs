using System.Windows.Threading;

namespace WitchDrawer.App.Infrastructure;

/// <summary>Debounces shell event bursts on the UI dispatcher; stops its timer after each repair.</summary>
internal sealed class DesktopLayerUpdateQueue : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _update;
    private readonly Action<Exception> _onError;
    private readonly DispatcherTimer _settleTimer;
    private int _queued;
    private int _disposed;
    private bool _updating;

    public DesktopLayerUpdateQueue(Dispatcher dispatcher, Action update, Action<Exception> onError)
    {
        _dispatcher = dispatcher;
        _update = update;
        _onError = onError;
        _settleTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _settleTimer.Tick += OnSettled;
    }

    public void Request()
    {
        if (Volatile.Read(ref _disposed) != 0 || _dispatcher.HasShutdownStarted
            || (_dispatcher.CheckAccess() && _updating)
            || Interlocked.Exchange(ref _queued, 1) != 0)
        {
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleAfterSettling);
        }
        catch (InvalidOperationException exception)
        {
            Volatile.Write(ref _queued, 0);
            if (!_dispatcher.HasShutdownStarted && Volatile.Read(ref _disposed) == 0)
            {
                _onError(exception);
            }
        }
    }

    private void ScheduleAfterSettling()
    {
        Volatile.Write(ref _queued, 0);
        if (Volatile.Read(ref _disposed) != 0 || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        // Win+D emits native notifications across multiple dispatcher turns.
        // Repair once after the latest event, rather than raising boxes between
        // Explorer's intermediate Z-order changes and hiding them again.
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void OnSettled(object? sender, EventArgs e)
    {
        _settleTimer.Stop();
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _updating = true;
        try
        {
            _update();
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            StopSettling();
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            try
            {
                _ = _dispatcher.BeginInvoke(DispatcherPriority.Send, StopSettling);
            }
            catch (InvalidOperationException)
            {
                // Dispatcher shutdown also stops delivery of timer callbacks.
            }
        }
    }

    private void StopSettling()
    {
        _settleTimer.Stop();
        _settleTimer.Tick -= OnSettled;
    }
}
