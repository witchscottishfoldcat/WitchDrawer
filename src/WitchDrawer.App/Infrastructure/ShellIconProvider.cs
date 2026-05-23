using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WitchDrawer.App.Infrastructure;

public static class ShellIconProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> IconTasks =
        new(StringComparer.OrdinalIgnoreCase);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    public static Task<ImageSource?> GetIconAsync(string? path, bool isDirectory, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var fullPath = Path.GetFullPath(path);
        var cacheKey = $"{(isDirectory ? "D" : "F")}|{size}|{fullPath}";
        var lazyTask = IconTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ImageSource?>>(
                () => LoadIconAsync(cacheKey, fullPath, isDirectory, size),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyTask.Value;
    }

    private static async Task<ImageSource?> LoadIconAsync(string cacheKey, string fullPath, bool isDirectory, int size)
    {
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
    }

    private static ImageSource? GetIcon(string fullPath, bool isDirectory, int size)
    {
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | (size <= 20 ? ShgfiSmallIcon : ShgfiLargeIcon);

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            flags |= ShgfiUseFileAttributes;
        }

        return GetIcon(fullPath, attributes, flags, size)
            ?? GetIcon(fullPath, attributes, flags | ShgfiUseFileAttributes, size);
    }

    private static ImageSource? GetIcon(string fullPath, uint attributes, uint flags, int size)
    {
        var result = SHGetFileInfo(
            fullPath,
            attributes,
            out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);

        if (result == nint.Zero || info.IconHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}

