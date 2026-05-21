using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Infrastructure;

public static class WindowMotion
{
    public static void PopIn(Window window, double fromScale = 0.985, int milliseconds = 150)
    {
        window.RenderTransformOrigin = new Point(0.5, 0.5);
        if (window.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            window.RenderTransform = scale;
        }

        window.Opacity = 0;
        scale.ScaleX = fromScale;
        scale.ScaleY = fromScale;

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration)
        {
            EasingFunction = ease
        });
    }
}
