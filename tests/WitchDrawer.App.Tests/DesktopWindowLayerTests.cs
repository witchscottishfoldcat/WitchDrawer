using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopWindowLayerTests
{
    [Theory]
    [InlineData(0u, true)]
    [InlineData(3u, true)]
    [InlineData(4u, false)]
    [InlineData(0x17u, false)]
    [InlineData(0x44u, true)]
    [InlineData(0x84u, false)]
    public void PositionFlags_IgnoreMoveResizeAndDeliberateHide(uint flags, bool expected) =>
        Assert.Equal(expected, DesktopWindowLayer.AffectsDesktopLayer(flags));

    [Theory]
    [InlineData(0x0005, 1, true)]
    [InlineData(0x0005, 0, false)]
    [InlineData(0x0005, 2, false)]
    [InlineData(0x0047, 0, false)]
    [InlineData(0x000F, 1, false)]
    public void Messages_RecognizeMinimizeButNotPaintOrResize(int message, int parameter, bool expected) =>
        Assert.Equal(expected, DesktopWindowLayer.IsLayerChangeMessage(message, parameter, 0));

    [Fact]
    public void Configure_ClearsWpfOwnerWithoutMovingOrShowingWindow() => DesktopLayerUpdateQueueTests.RunSta(() =>
    {
        var owner = CreateWindow();
        var box = CreateWindow();
        try
        {
            var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
            var handle = new WindowInteropHelper(box).EnsureHandle();
            User32Interop.SetWindowLongPtr(handle, -8, ownerHandle);
            User32Interop.GetWindowRect(handle, out var before);
            DesktopWindowLayer.Configure(handle);
            Assert.Equal(nint.Zero, GetWindow(handle, 4));
            Assert.False(IsWindowVisible(handle));
            Assert.False(box.IsActive);
            Assert.Equal(0L, GetWindowLongPtrW(handle, -20).ToInt64() & 8);
            Assert.NotEqual(0L, GetWindowLongPtrW(handle, -20).ToInt64() & 0x08000000);
            User32Interop.GetWindowRect(handle, out var after);
            Assert.Equal(before, after);
        }
        finally
        {
            box.Close();
            owner.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repair_OrdersBoxesAboveSurfaceAndBelowApplication_WithoutTopmost(bool appTopmost) =>
        DesktopLayerUpdateQueueTests.RunSta(() =>
        {
            var host = CreateWindow();
            var app = CreateWindow();
            var first = CreateWindow();
            var second = CreateWindow();
            try
            {
                app.Topmost = appTopmost;
                var hostHandle = new WindowInteropHelper(host).EnsureHandle();
                var appHandle = new WindowInteropHelper(app).EnsureHandle();
                var a = new WindowInteropHelper(first).EnsureHandle();
                var b = new WindowInteropHelper(second).EnsureHandle();
                DesktopWindowLayer.Configure(a);
                DesktopWindowLayer.Configure(b);
                first.Show();
                second.Show();
                host.Show();
                app.Show();
                Position(a, 1);
                Position(b, 1);
                Position(hostHandle, 0);
                if (!appTopmost) Position(appHandle, 0);
                User32Interop.GetWindowRect(a, out var before);

                DesktopWindowLayer.MaintainAboveHost(hostHandle, [a, b]);
                Assert.Equal(a, GetWindow(hostHandle, 3));
                Assert.Equal(b, GetWindow(a, 3));
                Assert.True(IsAbove(appHandle, b));
                foreach (var handle in new[] { a, b })
                {
                    Assert.Equal(nint.Zero, GetWindow(handle, 4));
                    Assert.Equal(0L, GetWindowLongPtrW(handle, -20).ToInt64() & 8);
                }
                Assert.False(first.IsActive);
                Assert.False(second.IsActive);
                User32Interop.GetWindowRect(a, out var after);
                Assert.Equal(before, after);

                // An already-correct band remains unchanged.
                DesktopWindowLayer.MaintainAboveHost(hostHandle, [a, b]);
                Assert.Equal(a, GetWindow(hostHandle, 3));
                Assert.Equal(b, GetWindow(a, 3));

                // A visible box minimized directly through Win32 is recovered.
                ShowWindow(a, 6);
                Assert.True(IsIconic(a));
                DesktopWindowLayer.MaintainAboveHost(hostHandle, [a, b]);
                Assert.False(IsIconic(a));
                Assert.True(IsWindowVisible(a));
                Assert.False(first.IsActive);
            }
            finally
            {
                first.Close();
                second.Close();
                app.Close();
                host.Close();
            }
        });

    private static Window CreateWindow() => new()
    {
        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        Width = 10, Height = 10, Left = -2000, Top = -2000
    };

    private static void Position(nint handle, nint after) =>
        User32Interop.SetWindowPos(handle, after, 0, 0, 0, 0, 0x0013);

    private static bool IsAbove(nint above, nint below)
    {
        for (var window = GetWindow(below, 3); window != 0; window = GetWindow(window, 3))
        {
            if (window == above) return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
