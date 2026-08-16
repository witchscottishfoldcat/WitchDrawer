using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Shell;

/// <summary>
/// 获取当前鼠标光标的物理屏幕坐标，用于 <see cref="ItemContextMenu"/> 精确定位
/// 原生菜单的弹出位置（<c>TrackPopupMenu</c> 需要的是屏幕像素坐标，不受 WPF 的
/// DIP/DPI 缩放与多显示器坐标空间影响）。
/// </summary>
public static class NativeCursor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    /// <summary>
    /// 返回当前光标物理屏幕坐标（像素）。失败时返回 <see langword="false"/>。
    /// </summary>
    public static bool TryGetCursorPos(out int x, out int y)
    {
        var success = GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return success;
    }
}
