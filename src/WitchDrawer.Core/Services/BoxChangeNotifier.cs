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
    public event EventHandler<BoxContentChangedEventArgs>? ContentChanged;

    internal void Publish(params Guid[] boxIds)
    {
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
}
