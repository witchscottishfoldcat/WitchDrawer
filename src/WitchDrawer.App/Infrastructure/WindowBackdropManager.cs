using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WitchDrawer.App.Infrastructure;

public static class WindowBackdropManager
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int DwmSystemBackdropNone = 1;
    private const int DwmSystemBackdropTransientWindow = 3;

    public static void Apply(Window window, AppTheme theme)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetIntAttribute(handle, DwmwaWindowCornerPreference, DwmWindowCornerPreferenceRound);
            SetIntAttribute(
                handle,
                DwmwaSystemBackdropType,
                theme == AppTheme.Glass || theme == AppTheme.Crystal ? DwmSystemBackdropTransientWindow : DwmSystemBackdropNone);
        }
        catch
        {
            // DWM backdrop is a visual enhancement only; unsupported systems should keep running.
        }
    }

    private static void SetIntAttribute(IntPtr handle, int attribute, int value)
    {
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
