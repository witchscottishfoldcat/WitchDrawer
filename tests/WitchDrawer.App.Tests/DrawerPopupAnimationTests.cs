using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DrawerPopupAnimationTests
{
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
