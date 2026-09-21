using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Infrastructure;

internal sealed class DrawerPopupAnimation(FrameworkElement surface, ScaleTransform scale)
{
    private int _generation;
    private ClockGroup? _clock;

    public void Prepare()
    {
        Stop();
        // Allocate the cache before Popup creates/renders its HWND. Placement still
        // uses the final size; a reduced scale here would displace the native popup.
        surface.CacheMode = new BitmapCache();
        surface.Opacity = 0;
    }

    public ClockGroup Start(double iconFrameSize)
    {
        var generation = ++_generation;
        var initialScaleX = Math.Clamp(iconFrameSize / Math.Max(1, surface.ActualWidth), 0.08, 0.24);
        var initialScaleY = Math.Clamp(iconFrameSize / Math.Max(1, surface.ActualHeight), 0.08, 0.32);
        var duration = TimeSpan.FromMilliseconds(190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        easing.Freeze();
        surface.CacheMode ??= new BitmapCache();
        surface.Opacity = 1;

        var timeline = new ParallelTimeline { Duration = duration };
        timeline.Children.Add(new DoubleAnimation(initialScaleX, 1, duration) { EasingFunction = easing });
        timeline.Children.Add(new DoubleAnimation(initialScaleY, 1, duration) { EasingFunction = easing });
        timeline.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(145)) { EasingFunction = easing });
        timeline.Freeze();
        var clock = (ClockGroup)timeline.CreateClock(true);
        _clock = clock;
        clock.Completed += (_, _) =>
        {
            if (generation == _generation)
            {
                // Opacity finishes first. Keep the cached visual until BOTH scale
                // clocks finish, then discard held clocks and render crisp text.
                Stop();
            }
        };
        scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, (AnimationClock)clock.Children[0]);
        scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, (AnimationClock)clock.Children[1]);
        surface.ApplyAnimationClock(UIElement.OpacityProperty, (AnimationClock)clock.Children[2]);
        return clock;
    }

    public void Stop()
    {
        _generation++;
        _clock?.Controller?.Remove();
        _clock = null;
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.Opacity = 1;
        ResetScale(scale);
        surface.CacheMode = null;
    }

    internal static void ResetScale(ScaleTransform scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
    }
}
