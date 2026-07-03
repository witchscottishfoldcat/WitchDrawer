using System.Collections.Concurrent;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

// Tracks how many consecutive reads have reported a stored item's backing file as missing.
// A single transient miss (file locked, network drive detached, AV quarantine) must not delete
// the user's record. Only after MissingThreshold consecutive misses do we prune.
internal sealed class MissingItemTracker
{
    internal const int MissingThreshold = 3;

    private readonly ConcurrentDictionary<Guid, int> _missCounts = [];

    public RecordOutcome Record(DrawerItem item, bool missing)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            return RecordOutcome.Keep;
        }

        if (!missing)
        {
            _missCounts.TryRemove(item.Id, out _);
            return RecordOutcome.Keep;
        }

        var count = _missCounts.AddOrUpdate(item.Id, 1, (_, current) => current + 1);
        if (count >= MissingThreshold)
        {
            _missCounts.TryRemove(item.Id, out _);
            return RecordOutcome.Prune;
        }

        return RecordOutcome.Keep;
    }

    internal enum RecordOutcome
    {
        Keep,
        Prune
    }
}
