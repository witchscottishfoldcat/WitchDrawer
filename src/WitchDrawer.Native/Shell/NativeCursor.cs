using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Shell;

public static class NativeCursor
{
    public static bool TryGetPosition(out int x, out int y)
    {
        var success = GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
