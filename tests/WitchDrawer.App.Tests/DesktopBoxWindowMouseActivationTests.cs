using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WitchDrawer.App.Views;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowMouseActivationTests
{
    [Theory]
    [InlineData(0x0200)] // WM_MOUSEMOVE: active window tracking / hover
    [InlineData(0x0201)] // WM_LBUTTONDOWN: ordinary mouse input
    public void MouseActivationHook_DeliversInputWithoutActivatingBox(int mouseMessage) =>
        DesktopLayerUpdateQueueTests.RunSta(() =>
        {
            var window = new Window
            {
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
                Width = 10, Height = 10, Left = -2000, Top = -2000
            };
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var source = HwndSource.FromHwnd(handle)!;

            // WM_MOUSEACTIVATE needs only _nativeWindow. Bind the production hook
            // to an isolated HWND without constructing the full box UI, an Application
            // singleton, or services that could open user data.
            var receiver = (DesktopBoxWindow)RuntimeHelpers.GetUninitializedObject(typeof(DesktopBoxWindow));
            typeof(DesktopBoxWindow).GetField("_nativeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(receiver, new DesktopToolWindow(handle));
            var method = typeof(DesktopBoxWindow).GetMethod("WindowMessageHook", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hook = (HwndSourceHook)method.CreateDelegate(typeof(HwndSourceHook), receiver);
            try
            {
                DesktopWindowLayer.Configure(handle);
                source.AddHook(hook);
                // Windows sends this even with WS_EX_NOACTIVATE when hover activation is enabled.
                var response = SendMessageW(handle, 0x0021, handle, (nint)((mouseMessage << 16) | 1));
                Assert.Equal((nint)3, response); // MA_NOACTIVATE preserves the mouse input.
                Assert.False(window.IsActive);
                if (DesktopWindowLayer.IsEnabled)
                {
                    Assert.Equal(nint.Zero, GetWindow(handle, 4)); // GW_OWNER: no Windows 11 ownership repair.
                }
            }
            finally
            {
                source.RemoveHook(hook);
                window.Close();
            }
        });

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, int message, nint wordParameter, nint longParameter);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
}
