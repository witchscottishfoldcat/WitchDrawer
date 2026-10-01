using System.Collections.Concurrent;
using System.IO;
using WitchDrawer.Native.Files;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WitchDrawer.App.Infrastructure;

public static class ShellIconProvider
{
    private const int MaxCachedIconEntries = 512;
    private const int MaxConcurrentIconLoads = 4;
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> IconTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<KeyValuePair<string, Lazy<Task<ImageSource?>>>> IconTaskOrder = new();
    private static readonly SemaphoreSlim IconLoadGate = new(MaxConcurrentIconLoads, MaxConcurrentIconLoads);

    public static Task<ImageSource?> GetIconAsync(string? path, bool isDirectory, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var fullPath = Path.GetFullPath(path);
        size = Math.Clamp(
            size,
            DpiAwareIconSize.MinimumSourcePixelSize,
            DpiAwareIconSize.MaximumSourcePixelSize);
        var cacheKey = $"{(isDirectory ? "D" : "F")}|{size}|{fullPath}";
        var createdTask = new Lazy<Task<ImageSource?>>(
            () => LoadIconAsync(cacheKey, fullPath, isDirectory, size),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazyTask = IconTasks.GetOrAdd(cacheKey, createdTask);
        var iconTask = lazyTask.Value;

        if (ReferenceEquals(lazyTask, createdTask))
        {
            _ = iconTask.ContinueWith(
                completedTask => TrackCompletedCacheEntry(cacheKey, createdTask, completedTask.Result),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return iconTask;
    }

    private static void TrackCompletedCacheEntry(
        string cacheKey,
        Lazy<Task<ImageSource?>> cacheEntry,
        ImageSource? icon)
    {
        if (icon is null
            || !IconTasks.TryGetValue(cacheKey, out var currentEntry)
            || !ReferenceEquals(cacheEntry, currentEntry))
        {
            return;
        }

        IconTaskOrder.Enqueue(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(cacheKey, cacheEntry));
        TrimIconCache();
    }

    private static void TrimIconCache()
    {
        while (IconTasks.Count > MaxCachedIconEntries && IconTaskOrder.TryDequeue(out var oldest))
        {
            IconTasks.TryRemove(oldest);
        }
    }

    private static async Task<ImageSource?> LoadIconAsync(string cacheKey, string fullPath, bool isDirectory, int size)
    {
        await IconLoadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var icon = await Task.Run(() => GetIcon(fullPath, isDirectory, size)).ConfigureAwait(false);
            if (icon is null)
            {
                IconTasks.TryRemove(cacheKey, out _);
            }

            return icon;
        }
        catch
        {
            IconTasks.TryRemove(cacheKey, out _);
            throw;
        }
        finally
        {
            IconLoadGate.Release();
        }
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
