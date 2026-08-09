using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Files;

public static class ShellChangeNotifier
{
    private const uint ShellChangeCreate = 0x00000002;
    private const uint ShellChangeMakeDirectory = 0x00000008;
    private const uint ShellChangeUpdateDirectory = 0x00001000;
    private const uint ShellChangeUpdateItem = 0x00002000;
    private const uint ShellNotifyPathW = 0x0005;
    private const uint ShellNotifyFlushNoWait = 0x2000;

    public static void NotifyCreated(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SHChangeNotify(
            GetCreateEvent(isDirectory),
            ShellNotifyPathW | ShellNotifyFlushNoWait,
            Path.GetFullPath(path),
            nint.Zero);
    }

    /// <summary>
    /// Notifies Explorer that a new item landed in a folder that users watch closely
    /// (the desktop in particular). In addition to the per-file create event, the owning
    /// directory is refreshed so the folder view redraws promptly, and for shortcuts the
    /// item itself is re-resolved so the icon cache catches up instead of lagging behind.
    /// </summary>
    public static void NotifyFolderItemCreated(string itemPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(itemPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(itemPath);

        // 1. Announce the new file/directory itself.
        NotifyCreated(fullPath, isDirectory);

        // 2. For shortcuts, ask Explorer to re-resolve this entry so its target icon is
        //    extracted without waiting for the next idle cache pass.
        if (!isDirectory && fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            SHChangeNotify(
                ShellChangeUpdateItem,
                ShellNotifyPathW | ShellNotifyFlushNoWait,
                fullPath,
                nint.Zero);
        }

        // 3. Refresh the containing directory view so the layout/icon grid repaints
        //    immediately. This is the event the desktop folder view reliably acts on.
        var parentDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            SHChangeNotify(
                ShellChangeUpdateDirectory,
                ShellNotifyPathW | ShellNotifyFlushNoWait,
                Path.GetFullPath(parentDirectory),
                nint.Zero);
        }
    }

    internal static uint GetCreateEvent(bool isDirectory) =>
        isDirectory ? ShellChangeMakeDirectory : ShellChangeCreate;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string item1,
        nint item2);
}
