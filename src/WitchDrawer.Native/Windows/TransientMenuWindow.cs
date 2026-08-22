using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Native lifetime helpers for a detached WPF menu window. This class knows
/// nothing about drawer items, themes, commands, or desktop-box ownership.
/// </summary>
public static class TransientMenuWindow
{
    private const int WindowOwnerIndex = -8;
    private const int WindowExtendedStyleIndex = -20;
    private const int LowLevelMouseHook = 14;
    private const int LowLevelKeyboardHook = 13;
    private const int MouseLeftButtonDown = 0x0201;
    private const int MouseRightButtonDown = 0x0204;
    private const int MouseMiddleButtonDown = 0x0207;
    private const int MouseExtraButtonDown = 0x020B;
    private const int KeyDown = 0x0100;
    private const int SystemKeyDown = 0x0104;
    private const int VirtualKeyEscape = 0x1B;
    private const int VirtualKeyD = 0x44;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint ExtendedStyleToolWindow = 0x00000080;
    private const uint ExtendedStyleNoActivate = 0x08000000;
    private static readonly nint WindowPositionNotTopMost = -2;

    public static void ConfigureNoActivate(nint handle)
    {
        if (handle == nint.Zero || !IsWindow(handle))
        {
            return;
        }

        SetWindowLongPtr(handle, WindowOwnerIndex, nint.Zero);
        var extendedStyle = unchecked((uint)(nuint)GetWindowLongPtr(handle, WindowExtendedStyleIndex));
        SetWindowLongPtr(
            handle,
            WindowExtendedStyleIndex,
            unchecked((nint)(extendedStyle | ExtendedStyleToolWindow | ExtendedStyleNoActivate)));
    }

    public static void PositionWithoutActivation(
        nint handle,
        int requestedX,
        int requestedY,
        int width,
        int height)
    {
        if (handle == nint.Zero || !IsWindow(handle))
        {
            return;
        }

        ConfigureNoActivate(handle);
        var bounds = GetWorkArea(requestedX, requestedY);
        var position = ClampToWorkArea(
            requestedX,
            requestedY,
            Math.Max(1, width),
            Math.Max(1, height),
            bounds.Left,
            bounds.Top,
            bounds.Right,
            bounds.Bottom);

        // HWND_NOTOPMOST explicitly removes stale topmost state. SWP_NOACTIVATE
        // keeps the menu completely outside the foreground/last-active-popup
        // chain used by the main window, desktop boxes, Explorer, and Win+D.
        SetWindowPos(
            handle,
            WindowPositionNotTopMost,
            position.X,
            position.Y,
            0,
            0,
            SetWindowPositionNoSize | SetWindowPositionNoActivate);
    }

    public static IDisposable DismissOnOutsideInput(nint handle, Action dismiss) =>
        new OutsideInputMonitor(handle, dismiss);

    internal static (int X, int Y) ClampToWorkArea(
        int x,
        int y,
        int width,
        int height,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom)
    {
        var maxX = Math.Max(workLeft, workRight - width);
        var maxY = Math.Max(workTop, workBottom - height);
        return (Math.Clamp(x, workLeft, maxX), Math.Clamp(y, workTop, maxY));
    }

    internal static bool IsOutside(int left, int top, int right, int bottom, int x, int y) =>
        x < left || x >= right || y < top || y >= bottom;

    internal static bool ShouldDismissForKey(uint virtualKey, bool windowsKeyDown) =>
        virtualKey == VirtualKeyEscape
        || (virtualKey == VirtualKeyD && windowsKeyDown);

    private static NativeRect GetWorkArea(int x, int y)
    {
        var point = new NativePoint { X = x, Y = y };
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        return monitor != nint.Zero && GetMonitorInfoW(monitor, ref info)
            ? info.WorkArea
            : new NativeRect
            {
                Left = 0,
                Top = 0,
                Right = GetSystemMetrics(0),
                Bottom = GetSystemMetrics(1)
            };
    }

    private sealed class OutsideInputMonitor : IDisposable
    {
        private readonly nint _windowHandle;
        private readonly Action _dismiss;
        private readonly MouseHookProcedure _mouseProcedure;
        private readonly KeyboardHookProcedure _keyboardProcedure;
        private nint _mouseHookHandle;
        private nint _keyboardHookHandle;
        private int _dismissQueued;

        public OutsideInputMonitor(nint windowHandle, Action dismiss)
        {
            _windowHandle = windowHandle;
            _dismiss = dismiss;
            _mouseProcedure = OnMouseInput;
            _keyboardProcedure = OnKeyboardInput;
            var module = GetModuleHandleW(null);
            _mouseHookHandle = SetWindowsHookExW(
                LowLevelMouseHook,
                _mouseProcedure,
                module,
                0);
            if (_mouseHookHandle == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _keyboardHookHandle = SetWindowsKeyboardHookExW(
                LowLevelKeyboardHook,
                _keyboardProcedure,
                module,
                0);
            if (_keyboardHookHandle == nint.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = nint.Zero;
                throw new Win32Exception(error);
            }
        }

        public void Dispose()
        {
            var mouseHookHandle = Interlocked.Exchange(ref _mouseHookHandle, nint.Zero);
            if (mouseHookHandle != nint.Zero)
            {
                UnhookWindowsHookEx(mouseHookHandle);
            }

            var keyboardHookHandle = Interlocked.Exchange(ref _keyboardHookHandle, nint.Zero);
            if (keyboardHookHandle != nint.Zero)
            {
                UnhookWindowsHookEx(keyboardHookHandle);
            }
        }

        private nint OnMouseInput(int code, nint message, nint dataAddress)
        {
            if (code >= 0
                && IsButtonDown(message)
                && GetWindowRect(_windowHandle, out var bounds))
            {
                var data = Marshal.PtrToStructure<LowLevelMouseData>(dataAddress);
                if (IsOutside(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right,
                        bounds.Bottom,
                        data.Point.X,
                        data.Point.Y))
                {
                    TryQueueDismiss();
                }
            }

            return CallNextHookEx(_mouseHookHandle, code, message, dataAddress);
        }

        private nint OnKeyboardInput(int code, nint message, nint dataAddress)
        {
            if (code >= 0 && IsKeyDown(message))
            {
                var data = Marshal.PtrToStructure<LowLevelKeyboardData>(dataAddress);
                var dismiss = ShouldDismissForKey(data.VirtualKey, IsWindowsKeyDown());
                if (dismiss)
                {
                    TryQueueDismiss();
                }
            }

            return CallNextHookEx(_keyboardHookHandle, code, message, dataAddress);
        }

        private static bool IsButtonDown(nint message)
        {
            var value = unchecked((int)(long)message);
            return value is MouseLeftButtonDown
                or MouseRightButtonDown
                or MouseMiddleButtonDown
                or MouseExtraButtonDown;
        }

        private static bool IsKeyDown(nint message)
        {
            var value = unchecked((int)(long)message);
            return value is KeyDown or SystemKeyDown;
        }

        private static bool IsWindowsKeyDown() =>
            (GetAsyncKeyState(VirtualKeyLeftWindows) & 0x8000) != 0
            || (GetAsyncKeyState(VirtualKeyRightWindows) & 0x8000) != 0;

        private bool TryQueueDismiss()
        {
            if (Interlocked.Exchange(ref _dismissQueued, 1) != 0)
            {
                return false;
            }

            _dismiss();
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private delegate nint MouseHookProcedure(int code, nint message, nint dataAddress);
    private delegate nint KeyboardHookProcedure(int code, nint message, nint dataAddress);

    private static nint GetWindowLongPtr(nint windowHandle, int index)
    {
        return nint.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : GetWindowLong32(windowHandle, index);
    }

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value)
    {
        return nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : SetWindowLong32(windowHandle, index, value);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(
        int hookIdentifier,
        MouseHookProcedure hookProcedure,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsKeyboardHookExW(
        int hookIdentifier,
        KeyboardHookProcedure hookProcedure,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint message,
        nint dataAddress);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref NativeMonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);
}
