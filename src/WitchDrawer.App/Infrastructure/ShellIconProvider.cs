using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App.Infrastructure;

public static class ShellIconProvider
{
    private static readonly ShellIconCache Cache = new(GetIcon);

    public static Task<ImageSource?> GetIconAsync(
        string? path,
        bool isDirectory,
        int size,
        CancellationToken cancellationToken = default)
    {
        return Cache.GetIconAsync(path, isDirectory, size, cancellationToken);
    }

    private static ImageSource? GetIcon(string fullPath, bool isDirectory, int size)
    {
        using var image = ShellIconExtractor.GetIcon(fullPath, isDirectory, size);
        if (image is null || image.IsInvalid) return null;
        var source = image.IsBitmap
            ? Imaging.CreateBitmapSourceFromHBitmap(image.DangerousGetHandle(), nint.Zero,
                Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size))
            : Imaging.CreateBitmapSourceFromHIcon(image.DangerousGetHandle(),
                Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
        source.Freeze();
        return source;
    }
}

// A caller owns demand only while it awaits its request. Cancelling one caller
// leaves shared work alive for the others, but abandons work that is still queued.
internal sealed class ShellIconCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Entry> _completedEntries = new();
    private readonly SemaphoreSlim _loadGate;
    private readonly Func<string, bool, int, ImageSource?> _extractIcon;
    private readonly int _maxCachedEntries;

    internal ShellIconCache(
        Func<string, bool, int, ImageSource?> extractIcon,
        int maxConcurrentLoads = 4,
        int maxCachedEntries = 512)
    {
        _extractIcon = extractIcon;
        _loadGate = new SemaphoreSlim(maxConcurrentLoads, maxConcurrentLoads);
        _maxCachedEntries = maxCachedEntries;
    }

    internal async Task<ImageSource?> GetIconAsync(
        string? path,
        bool isDirectory,
        int size,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        size = Math.Clamp(size, DpiAwareIconSize.MinimumSourcePixelSize, DpiAwareIconSize.MaximumSourcePixelSize);
        var cacheKey = $"{(isDirectory ? "D" : "F")}|{size}|{fullPath}";
        Entry entry;
        bool created;
        lock (_sync)
        {
            created = !_entries.TryGetValue(cacheKey, out entry!);
            if (created)
            {
                entry = new Entry(cacheKey, fullPath, isDirectory, size);
                _entries.Add(cacheKey, entry);
            }

            entry.Consumers++;
        }

        if (created)
        {
            _ = LoadIconAsync(entry);
        }

        try
        {
            return await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                entry.Consumers--;
                if (entry.Consumers == 0 && !entry.Started && !entry.Completion.Task.IsCompleted)
                {
                    RemoveEntry(entry);
                    entry.QueuedCancellation.Cancel();
                }
            }
        }
    }

    private async Task LoadIconAsync(Entry entry)
    {
        var gateEntered = false;
        try
        {
            await _loadGate.WaitAsync(entry.QueuedCancellation.Token).ConfigureAwait(false);
            gateEntered = true;
            var icon = await Task.Run(() =>
            {
                lock (_sync)
                {
                    // Demand can disappear after admission, before a worker starts.
                    entry.QueuedCancellation.Token.ThrowIfCancellationRequested();
                    entry.Started = true;
                }

                var extracted = _extractIcon(entry.FullPath, entry.IsDirectory, entry.Size);
                extracted?.Freeze();
                return extracted;
            }, entry.QueuedCancellation.Token).ConfigureAwait(false);

            lock (_sync)
            {
                if (icon is null)
                {
                    RemoveEntry(entry);
                }
                else
                {
                    _completedEntries.Enqueue(entry);
                    while (_completedEntries.Count > _maxCachedEntries)
                    {
                        RemoveEntry(_completedEntries.Dequeue());
                    }
                }

                entry.Completion.TrySetResult(icon);
            }
        }
        catch (OperationCanceledException) when (entry.QueuedCancellation.IsCancellationRequested)
        {
            entry.Completion.TrySetCanceled(entry.QueuedCancellation.Token);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                RemoveEntry(entry);
                entry.Completion.TrySetException(exception);
            }
        }
        finally
        {
            if (gateEntered)
            {
                _loadGate.Release();
            }

            entry.QueuedCancellation.Dispose();
        }
    }

    private void RemoveEntry(Entry entry)
    {
        if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
        {
            _entries.Remove(entry.Key);
        }
    }

    private sealed class Entry(string key, string fullPath, bool isDirectory, int size)
    {
        internal string Key { get; } = key;
        internal string FullPath { get; } = fullPath;
        internal bool IsDirectory { get; } = isDirectory;
        internal int Size { get; } = size;
        internal int Consumers { get; set; }
        internal bool Started { get; set; }
        internal CancellationTokenSource QueuedCancellation { get; } = new();
        internal TaskCompletionSource<ImageSource?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
