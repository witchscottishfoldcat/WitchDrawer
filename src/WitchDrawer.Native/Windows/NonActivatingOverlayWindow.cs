using System.Runtime.InteropServices;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Applies native styles for visual-only overlay windows that must never
/// activate or intercept mouse input.
/// </summary>
public static class NonActivatingOverlayWindow
{
    public const int NonClientHitTestMessage = 0x0084;

    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleTransparent = 0x00000020;
    private const long ExtendedStyleToolWindow = 0x00000080;
    private const long ExtendedStyleAppWindow = 0x00040000;
    private const long ExtendedStyleNoActivate = 0x08000000;
    private const uint SetWindowPositionFrameChanged = 0x0020;

    public static nint TransparentHitTestResult => (nint)(-1);

    public static nint ApplyRequiredExtendedStyles(nint extendedStyle)
    {
        var result = extendedStyle.ToInt64();
        result |= ExtendedStyleTransparent
            | ExtendedStyleToolWindow
            | ExtendedStyleNoActivate;
        result &= ~ExtendedStyleAppWindow;
        return (nint)result;
    }

    public static bool IsNonClientHitTestMessage(int message)
    {
        return message == NonClientHitTestMessage;
    }

    public static void Configure(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        var extendedStyle = GetWindowLongPtr(windowHandle, ExtendedStyleIndex);
        SetWindowLongPtr(
            windowHandle,
            ExtendedStyleIndex,
            ApplyRequiredExtendedStyles(extendedStyle));
        SetWindowPos(
            windowHandle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SetWindowPosNoSize
            | SetWindowPosNoMove
            | SetWindowPosNoZOrder
            | SetWindowPosNoActivate
            | SetWindowPositionFrameChanged);
    }

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);
}
