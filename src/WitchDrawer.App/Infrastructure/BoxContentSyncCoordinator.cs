using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed record BoxRefreshRequest(IReadOnlyList<Guid>? BoxIds, bool PresentationOnly = false)
{
    public static BoxRefreshRequest All { get; } = new(BoxIds: null);
    public bool Affects(Guid boxId) => BoxIds is null || BoxIds.Contains(boxId);

    public BoxRefreshRequest Merge(BoxRefreshRequest other)
        => new(BoxIds is null || other.BoxIds is null
                ? null : BoxIds.Concat(other.BoxIds).Distinct().ToArray(),
            PresentationOnly && other.PresentationOnly);
}

public sealed class BoxesChangedEventArgs(Guid? boxId = null, bool presentationOnly = false) : EventArgs
{
    public Guid? BoxId { get; } = boxId;
    public BoxRefreshRequest RefreshRequest { get; } = new(
        boxId is Guid id ? [id] : null, presentationOnly);
}

public interface IBoxContentRefreshTarget
{
    Task RefreshContentAsync(BoxRefreshRequest request);
}

/// <summary>Coalesces changes on the UI dispatcher and refreshes each surface independently.</summary>
public sealed class BoxContentSyncCoordinator : IDisposable
{
    private readonly BoxChangeNotifier _changes;
    private readonly IReadOnlyList<IBoxContentRefreshTarget> _targets;
    private readonly Action<Action> _dispatch;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private BoxRefreshRequest? _pending;
    private TaskCompletionSource? _idle;
    private bool _scheduled;
    private bool _disposed;

    public BoxContentSyncCoordinator(BoxChangeNotifier changes,
        IReadOnlyList<IBoxContentRefreshTarget> targets, Action<Action> dispatch, IAppLogger logger)
    {
        _changes = changes;
        _targets = targets;
        _dispatch = dispatch;
        _logger = logger;
        changes.ContentChanged += OnContentChanged;
    }

    public Task WhenIdleAsync()
    {
        lock (_gate) return _idle?.Task ?? Task.CompletedTask;
    }

    private void OnContentChanged(object? sender, BoxContentChangedEventArgs e)
        => Request(new(e.BoxIds));

    public void Request(BoxRefreshRequest request)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = _pending is null ? request : _pending.Merge(request);
            if (_scheduled) return;
            _scheduled = true;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { _dispatch(() => _ = DrainAsync()); }
        catch
        {
            lock (_gate) { _pending = null; _scheduled = false; _idle?.TrySetResult(); }
            throw;
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            BoxRefreshRequest request;
            lock (_gate)
            {
                if (_disposed || _pending is null)
                {
                    _pending = null;
                    _scheduled = false;
                    _idle?.TrySetResult();
                    return;
                }
                request = _pending;
                _pending = null;
            }

            foreach (var target in _targets)
            {
                lock (_gate) { if (_disposed) break; }
                try { await target.RefreshContentAsync(request); }
                catch (Exception exception)
                {
                    try { _logger.Error(exception, "Failed to synchronize box content."); }
                    catch { }
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = null;
            _idle?.TrySetResult();
        }
        _changes.ContentChanged -= OnContentChanged;
    }
}
