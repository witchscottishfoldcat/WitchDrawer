using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DrawerPopupAnimationTests
{
    [Fact]
    public void Animation_KeepsCacheAfterFadeUntilScaleCompletes()
    {
        RunSta(host =>
        {
            var surface = new Border { Width = 250, Height = 200 };
            surface.Measure(new Size(250, 200));
            surface.Arrange(new Rect(0, 0, 250, 200));
            var scale = new ScaleTransform();
            surface.RenderTransform = scale;
            host.Content = surface;
            var animation = new DrawerPopupAnimation(surface, scale);
            animation.Prepare();
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
            Assert.Equal(0, surface.Opacity);
            var cache = Assert.IsType<BitmapCache>(surface.CacheMode);

            var clock = animation.Start(36);
            clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(160), TimeSeekOrigin.BeginTime);
            Assert.Equal(1, surface.Opacity);
            Assert.InRange(scale.ScaleX, 0.9, 0.9999);
            Assert.Same(cache, surface.CacheMode);

            clock.Controller.SkipToFill();
            PumpDispatcherUntil(() => surface.CacheMode is null);
            Assert.Null(surface.CacheMode);
            Assert.False(surface.HasAnimatedProperties);
            Assert.False(scale.HasAnimatedProperties);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
            Assert.Equal(1, surface.Opacity);
        });
    }

    [Fact]
    public void CloseAndReopen_OldClockCannotClearNewAnimationCache()
    {
        RunSta(host =>
        {
            var surface = new Border { Width = 250, Height = 200 };
            surface.Measure(new Size(250, 200));
            surface.Arrange(new Rect(0, 0, 250, 200));
            var scale = new ScaleTransform();
            surface.RenderTransform = scale;
            host.Content = surface;
            var animation = new DrawerPopupAnimation(surface, scale);
            animation.Prepare();
            var first = animation.Start(36);
            first.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(80), TimeSeekOrigin.BeginTime);
            animation.Stop();
            Assert.Null(surface.CacheMode);
            Assert.False(scale.HasAnimatedProperties);
            animation.Prepare();
            var cache = surface.CacheMode;
            var second = animation.Start(36);
            first.Controller.SkipToFill();
            PumpDispatcherUntil(() => second.CurrentTime > TimeSpan.Zero);
            Assert.Same(cache, surface.CacheMode);
            second.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(80), TimeSeekOrigin.BeginTime);
            Assert.True(scale.ScaleX < 1);
            animation.Stop();
            Assert.False(surface.HasAnimatedProperties);
            Assert.False(scale.HasAnimatedProperties);
        });
    }

    private static void PumpDispatcherUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(5)
        };
        timer.Tick += (_, _) =>
        {
            if (completed() || watch.Elapsed > TimeSpan.FromSeconds(2)) frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(completed(), "Animation clock did not reach the expected state.");
    }

    private static void RunSta(Action<Window> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var host = new Window
            {
                Width = 300, Height = 250, Left = -10000, Top = -10000,
                ShowInTaskbar = false, ShowActivated = false
            };
            try { host.Show(); test(host); }
            catch (Exception exception) { failure = exception; }
            finally { host.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void PrepareForPlacement_ClearsHeldAnimationAndUsesNeutralScale()
    {
        var scale = new ScaleTransform(1, 1);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.12, 0.12, Duration.Forever));
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.12, 0.12, Duration.Forever));

        DesktopBoxWindow.PrepareDrawerPopupScaleForPlacement(scale);

        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(1, scale.ScaleY);
    }
}
