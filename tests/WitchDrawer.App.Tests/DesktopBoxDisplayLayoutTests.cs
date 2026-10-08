using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxDisplayLayoutTests
{
    [Theory]
    [InlineData(0x007E, 32)]
    [InlineData(0x001A, 0x002F)]
    public void NativeDisplayNotification_QueuesOneDeferredRepairWithoutConsumingMessage(int message, int parameter)
        => DesktopLayerUpdateQueueTests.RunSta(() =>
        {
            var receiver = CreateHookReceiver();
            var hook = (HwndSourceHook)typeof(DesktopBoxWindow)
                .GetMethod("WindowMessageHook", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(HwndSourceHook), receiver);
            try
            {
                var handled = false;
                hook(nint.Zero, message, (nint)parameter, nint.Zero, ref handled);
                Assert.False(handled); // WPF still processes the original notification.
                Assert.True(Field<bool>(receiver, "_isDisplayLayoutRecoveryPending"));
                var timer = Field<DispatcherTimer>(receiver, "_displayLayoutTimer");
                for (var i = 0; i < 20; i++)
                {
                    hook(nint.Zero, message, (nint)parameter, nint.Zero, ref handled);
                    Assert.Same(timer, Field<DispatcherTimer>(receiver, "_displayLayoutTimer"));
                }
                Assert.True(timer.IsEnabled);
            }
            finally { StopRecovery(receiver); }
        });

    [Fact]
    public void ActiveTransition_DefersRepairAndCloseCancelsQueuedWork() => DesktopLayerUpdateQueueTests.RunSta(() =>
    {
        var receiver = CreateHookReceiver();
        SetField(receiver, "_isVisibleBoundsClampingEnabled", true);
        SetField(receiver, "_isRollTransitioning", true);
        try
        {
            receiver.RequestDisplayLayoutRecovery();
            var tick = typeof(DesktopBoxWindow).GetMethod("OnDisplayLayoutTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            tick.Invoke(receiver, [null, EventArgs.Empty]);
            var timer = Field<DispatcherTimer>(receiver, "_displayLayoutTimer");
            Assert.True(timer.IsEnabled);
            Assert.True(Field<bool>(receiver, "_isDisplayLayoutRecoveryPending"));
            SetField(receiver, "_forceClose", true);
            tick.Invoke(receiver, [null, EventArgs.Empty]);
            Assert.False(timer.IsEnabled);
            StopRecovery(receiver);
            receiver.RequestDisplayLayoutRecovery();
            Assert.Null(Field<object?>(receiver, "_displayLayoutTimer"));
        }
        finally { StopRecovery(receiver); }
    });

    [Fact]
    public void LowerGameResolutionThenDesktopRestore_ReturnsToOriginalOriginWithoutDrift()
    {
        var layout = new DesktopBoxDisplayLayout();
        var original = new Point(1500, 800);
        layout.RememberPosition(original);
        var size = new Size(312, 212);
        var margin = new Thickness(6);
        var dpi = new DpiScale(1, 1);
        for (var cycle = 0; cycle < 20; cycle++)
        {
            Assert.Equal(new Point(974, 474), layout.ResolveOrigin(size, margin, dpi, new Rect(0, 0, 1280, 680)));
            Assert.Equal(original, layout.PreferredOriginPixels);
            Assert.Equal(original, layout.ResolveOrigin(size, margin, dpi, new Rect(0, 0, 1920, 1040)));
        }
    }

    [Theory]
    [InlineData(-1850, 180, -1920, 0)]
    [InlineData(2100, 180, 1920, 0)]
    [InlineData(120, 1300, 0, 1080)]
    public void ReconnectedMonitor_RestoresExtendedDesktopCoordinates(double x, double y, double left, double top)
    {
        var layout = new DesktopBoxDisplayLayout();
        var original = new Point(x, y);
        layout.RememberPosition(original);
        var size = new Size(300, 200);
        var margin = new Thickness(6);
        var dpi = new DpiScale(1, 1);
        var fallback = layout.ResolveOrigin(size, margin, dpi, new Rect(0, 0, 1920, 1040));
        Assert.NotEqual(original, fallback);
        Assert.Equal(original, layout.ResolveOrigin(size, margin, dpi, new Rect(left, top, 1920, 1040)));
    }

    [Fact]
    public void DpiRoundTrip_PreservesConfiguredSizeAndPreferredPhysicalOrigin()
    {
        var layout = new DesktopBoxDisplayLayout();
        var original = new Point(1500, 800);
        layout.RememberPosition(original);
        Assert.NotEqual(original, layout.ResolveOrigin(new Size(468, 318), new Thickness(6),
            new DpiScale(1.5, 1.5), new Rect(0, 0, 1280, 680)));
        Assert.Equal(original, layout.ResolveOrigin(new Size(312, 212), new Thickness(6),
            new DpiScale(1, 1), new Rect(0, 0, 1920, 1040)));
    }

    [Fact]
    public void ExplicitUserMove_ReplacesPreferredOriginDuringTemporaryResolution()
    {
        var layout = new DesktopBoxDisplayLayout();
        layout.RememberPosition(new Point(1500, 800));
        layout.ResolveOrigin(new Size(312, 212), new Thickness(6), new DpiScale(1, 1), new Rect(0, 0, 1280, 680));
        var moved = new Point(100, 120);
        layout.RememberPosition(moved);
        Assert.Equal(moved, layout.ResolveOrigin(new Size(312, 212), new Thickness(6),
            new DpiScale(1, 1), new Rect(0, 0, 1920, 1040)));
    }

    [Fact]
    public void MissingWorkArea_DoesNotDiscardPreferredOrigin()
    {
        var layout = new DesktopBoxDisplayLayout();
        layout.RememberPosition(new Point(1500, 800));
        Assert.Null(layout.ResolveOrigin(new Size(312, 212), new Thickness(6), new DpiScale(1, 1), Rect.Empty));
        Assert.Equal(new Point(1500, 800), layout.PreferredOriginPixels);
    }

    [Fact]
    public void ShutdownAndBackup_PersistPreferredOriginInsteadOfTemporaryCoordinates()
    {
        var layout = new DesktopBoxDisplayLayout();
        layout.RememberPosition(new Point(1500, 800));
        layout.ResolveOrigin(new Size(312, 212), new Thickness(6), new DpiScale(1, 1), new Rect(0, 0, 1280, 680));
        // An isolated receiver has no HWND. The production save paths must use the
        // preferred origin, including when the shell has moved or rebuilt its HWND.
        var receiver = (DesktopBoxWindow)RuntimeHelpers.GetUninitializedObject(typeof(DesktopBoxWindow));
        typeof(DesktopBoxWindow).GetField("_displayLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(receiver, layout);
        var stored = typeof(DesktopBoxManager).GetMethod("CaptureStoredPosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [receiver]);
        Assert.Equal("px:1500,800", stored);
        var id = Guid.NewGuid();
        var backup = (DesktopBoxManager.LayoutBackupPosition)typeof(DesktopBoxManager)
            .GetMethod("CaptureLayoutBackupPosition", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [id, receiver])!;
        Assert.Equal(new DesktopBoxManager.LayoutBackupPosition(id, 1500, 800, true), backup);
    }

    [Fact]
    public void DisplayRecovery_RemeasuresContentAfterExternalWindowResize() => DesktopLayerUpdateQueueTests.RunSta(() =>
    {
        var content = new Border { Width = 400, Height = 240 };
        var window = new Window
        {
            Content = content, Width = 180, Height = 120, SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(180, window.ActualWidth);
            DesktopBoxWindow.RestoreContentSizingAfterDisplayChange(window, content);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            Assert.Equal(400, window.ActualWidth);
            Assert.Equal(240, window.ActualHeight);
            Assert.Equal(400, content.Width);
            Assert.Equal(240, content.Height);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SurfaceClickAndSizeOnlyChanges_DoNotReplaceTheUserPosition()
    {
        var before = new Rect(974, 474, 312, 212);
        Assert.False(DesktopBoxWindow.HasPositionChanged(before, before));
        Assert.False(DesktopBoxWindow.HasPositionChanged(before, new Rect(974, 474, 468, 318)));
        Assert.True(DesktopBoxWindow.HasPositionChanged(before, new Rect(100, 120, 312, 212)));
    }

    [Fact]
    public void OrdinaryMouseUp_DoesNotStartAnotherPositionWrite()
    {
        // A click needs only to hide guides. Neither a live HWND nor the services
        // used to capture/write a position should be accessed by this event.
        var manager = (DesktopBoxManager)RuntimeHelpers.GetUninitializedObject(typeof(DesktopBoxManager));
        var window = (DesktopBoxWindow)RuntimeHelpers.GetUninitializedObject(typeof(DesktopBoxWindow));
        var handler = typeof(DesktopBoxManager).GetMethod("OnWindowMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(handler.Invoke(manager, [window, null]));
    }

    private static DesktopBoxWindow CreateHookReceiver()
    {
        // The notification path needs only a dispatcher and the preferred origin.
        // Avoid an Application singleton, full visual tree or access to user data.
        var receiver = (DesktopBoxWindow)RuntimeHelpers.GetUninitializedObject(typeof(DesktopBoxWindow));
        typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(receiver, Dispatcher.CurrentDispatcher);
        var layout = new DesktopBoxDisplayLayout();
        layout.RememberPosition(new Point(1500, 800));
        SetField(receiver, "_displayLayout", layout);
        return receiver;
    }

    private static T Field<T>(DesktopBoxWindow receiver, string name) =>
        (T)typeof(DesktopBoxWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver)!;

    private static void SetField(DesktopBoxWindow receiver, string name, object value) =>
        typeof(DesktopBoxWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(receiver, value);

    private static void StopRecovery(DesktopBoxWindow receiver) =>
        typeof(DesktopBoxWindow).GetMethod("StopDisplayLayoutRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(receiver, null);
}
