using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct NativePoint { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NativeMonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect WorkArea;
    public uint Flags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
}

public static class Program
{
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    public static void Main()
    {
        Console.WriteLine($"Marshal.SizeOf<NativeMonitorInfo>() = {Marshal.SizeOf<NativeMonitorInfo>()}");
        var monitor = MonitorFromPoint(new NativePoint(), MonitorDefaultToNearest);
        Console.WriteLine($"MonitorFromPoint = 0x{monitor:X}");
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        var ok = GetMonitorInfo(monitor, ref info);
        Console.WriteLine($"GetMonitorInfo ok={ok} work=({info.WorkArea.Left},{info.WorkArea.Top},{info.WorkArea.Right},{info.WorkArea.Bottom})");
    }
}
