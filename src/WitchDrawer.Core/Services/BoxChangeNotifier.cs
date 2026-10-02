using WitchDrawer.Core.Logging;

namespace WitchDrawer.Core.Services;

public sealed class BoxContentChangedEventArgs : EventArgs
{
    public BoxContentChangedEventArgs(IEnumerable<Guid> boxIds)
        => BoxIds = Array.AsReadOnly(boxIds.Distinct().ToArray());

    public IReadOnlyList<Guid> BoxIds { get; }
}

/// <summary>Publishes committed content changes, independently of the UI that initiated them.</summary>
public sealed class BoxChangeNotifier(IAppLogger? logger = null)
{
    private readonly AsyncLocal<ChangeBatch?> _currentBatch = new();

    public event EventHandler<BoxContentChangedEventArgs>? ContentChanged;

    /// <summary>Combines notifications from an awaited operation, including its background work.</summary>
    public IDisposable BeginBatch()
    {
        var batch = new ChangeBatch(this, _currentBatch.Value);
        _currentBatch.Value = batch;
        return batch;
    }

    internal void Publish(params Guid[] boxIds)
    {
        PublishToBatchOrSubscribers(_currentBatch.Value, boxIds);
    }

    private void PublishToBatchOrSubscribers(ChangeBatch? batch, Guid[] boxIds)
    {
        if (boxIds.Length == 0) return;
        for (; batch is not null; batch = batch.Parent)
        {
            if (batch.TryAdd(boxIds)) return;
        }

        var change = new BoxContentChangedEventArgs(boxIds);
        if (ContentChanged is not { } handlers) return;
        foreach (EventHandler<BoxContentChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, change); }
            catch (Exception exception)
            {
                // A committed file/database operation must not appear to fail because a view failed to refresh.
                try { logger?.Error(exception, "Failed to deliver a committed box content change."); }
                catch { }
            }
        }
    }

    private sealed class ChangeBatch(BoxChangeNotifier owner, ChangeBatch? parent) : IDisposable
    {
        private readonly object _gate = new();
        private readonly HashSet<Guid> _changed = [];
        private bool _disposed;
        public ChangeBatch? Parent { get; } = parent;

        public bool TryAdd(Guid[] boxIds)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                _changed.UnionWith(boxIds);
                return true;
            }
        }

        public void Dispose()
        {
            Guid[] changed;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                changed = _changed.ToArray();
            }

            if (ReferenceEquals(owner._currentBatch.Value, this))
                owner._currentBatch.Value = Parent;
            owner.PublishToBatchOrSubscribers(Parent, changed);
        }
    }
}
