using System.Collections.Concurrent;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Plugins;

/// <summary>Serializes plugin storage and lets existing services follow a promoted data directory.</summary>
internal sealed class PluginStorageCoordinator
{
    private static readonly ConcurrentDictionary<string, PluginStorageCoordinator> Roots =
        new(StringComparer.OrdinalIgnoreCase);
    private static long _nextOrder;
    private readonly long _order = Interlocked.Increment(ref _nextOrder);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginStorageCoordinator? _redirect;
    private string _directory;

    private PluginStorageCoordinator(string directory) => _directory = directory;

    public static PluginStorageCoordinator ForDataDirectory(string directory)
    {
        var fullPath = Normalize(Path.Combine(directory, "Plugins"));
        return Roots.GetOrAdd(PathSafety.GetComparisonPath(fullPath), _ => new(fullPath));
    }

    public string Directory => Resolve()._directory;

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private PluginStorageCoordinator Resolve()
    {
        var current = this;
        while (Volatile.Read(ref current._redirect) is { } next) current = next;
        return current;
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var current = Resolve();
            await current._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (current._redirect is null) return new Lease(current);
            current._gate.Release();
        }
    }

    public static async Task<MigrationLease> BeginMigrationAsync(
        string sourceDirectory, string targetDirectory, CancellationToken cancellationToken)
    {
        var sourceEntry = ForDataDirectory(sourceDirectory);
        var targetEntry = ForDataDirectory(targetDirectory);
        var sourcePath = Normalize(Path.Combine(sourceDirectory, "Plugins"));
        var targetPath = Normalize(Path.Combine(targetDirectory, "Plugins"));
        while (true)
        {
            var source = sourceEntry.Resolve();
            var target = targetEntry.Resolve();
            // A stable order also permits moving back to an earlier, now-empty location.
            var first = source._order <= target._order ? source : target;
            var second = ReferenceEquals(source, target) ? null : ReferenceEquals(first, source) ? target : source;
            await first._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (second is not null) await second._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { first._gate.Release(); throw; }

            if (!ReferenceEquals(sourceEntry.Resolve(), source) || !ReferenceEquals(targetEntry.Resolve(), target))
            {
                second?._gate.Release();
                first._gate.Release();
                continue;
            }
            if (!PathSafety.PathsEqual(source._directory, sourcePath))
            {
                second?._gate.Release();
                first._gate.Release();
                throw new InvalidOperationException("The data directory has already changed; restart before migrating again.");
            }
            return new MigrationLease(source, target, targetPath, first, second);
        }
    }

    private sealed class Lease(PluginStorageCoordinator owner) : IDisposable
    {
        public void Dispose() => owner._gate.Release();
    }

    internal sealed class MigrationLease(
        PluginStorageCoordinator source, PluginStorageCoordinator target, string targetPath,
        PluginStorageCoordinator first, PluginStorageCoordinator? second) : IDisposable
    {
        public void Promote()
        {
            source._directory = targetPath;
            if (!ReferenceEquals(source, target)) Volatile.Write(ref target._redirect, source);
        }

        public void Dispose()
        {
            second?._gate.Release();
            first._gate.Release();
        }
    }
}
