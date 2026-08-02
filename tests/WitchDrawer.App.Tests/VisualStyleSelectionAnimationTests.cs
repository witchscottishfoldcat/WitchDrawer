using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WitchDrawer.App.Tests;

public sealed class VisualStyleSelectionAnimationTests
{
    [Fact]
    public void EnsureAnimatableScaleTransform_ClonesFrozenStyleTransform()
    {
        var frozenTransform = new ScaleTransform(1.2, 0.8);
        frozenTransform.Freeze();

        var result = MainWindow.EnsureAnimatableScaleTransform(frozenTransform);

        Assert.NotSame(frozenTransform, result);
        Assert.False(result.IsFrozen);
        Assert.Equal(1.2, result.ScaleX);
        Assert.Equal(0.8, result.ScaleY);
        result.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 1.07, TimeSpan.FromMilliseconds(85)));
    }

    [Fact]
    public void EnsureAnimatableScaleTransform_ReusesMutableTransform()
    {
        var mutableTransform = new ScaleTransform(1, 1);

        var result = MainWindow.EnsureAnimatableScaleTransform(mutableTransform);

        Assert.Same(mutableTransform, result);
    }

    [Fact]
    public void EnsureAnimatableScaleTransform_CreatesDefaultWhenMissing()
    {
        var result = MainWindow.EnsureAnimatableScaleTransform(null);

        Assert.False(result.IsFrozen);
        Assert.Equal(1, result.ScaleX);
        Assert.Equal(1, result.ScaleY);
    }
}
