using System.Windows.Media;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class AppThemeManagerTests
{
    [Fact]
    public void SetBoxOpacity_RemembersEachThemeAndRaisesOnlyForChanges()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var changes = new List<ThemeBoxOpacityChangedEventArgs>();
        EventHandler<ThemeBoxOpacityChangedEventArgs> handler = (_, change) => changes.Add(change);
        AppThemeManager.BoxOpacityChanged += handler;

        try
        {
            AppThemeManager.SetBoxOpacity(AppTheme.Moe, 0.65);
            AppThemeManager.SetBoxOpacity(AppTheme.Moe, 0.65);
            AppThemeManager.SetBoxOpacity(AppTheme.Glass, 0.80);

            Assert.Equal(0.65, AppThemeManager.GetBoxOpacity(AppTheme.Moe), 3);
            Assert.Equal(0.80, AppThemeManager.GetBoxOpacity(AppTheme.Glass), 3);
            Assert.Equal(AppThemeManager.DefaultBoxOpacity, AppThemeManager.GetBoxOpacity(AppTheme.Crystal), 3);
            Assert.Collection(
                changes,
                change =>
                {
                    Assert.Equal(AppTheme.Moe, change.Theme);
                    Assert.Equal(0.65, change.Opacity, 3);
                },
                change =>
                {
                    Assert.Equal(AppTheme.Glass, change.Theme);
                    Assert.Equal(0.80, change.Opacity, 3);
                });
        }
        finally
        {
            AppThemeManager.BoxOpacityChanged -= handler;
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public void DefaultOpacity_ReproducesLegacySecondStageCrystalColors()
    {
        var surface = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "GlassSurfaceBrush",
            AppThemeManager.DefaultBoxOpacity);
        var panel = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "PanelBrush",
            AppThemeManager.DefaultBoxOpacity);
        var fullyOpaqueProfile = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "GlassSurfaceBrush",
            AppThemeManager.MaximumBoxOpacity);

        Assert.Equal((Color)ColorConverter.ConvertFromString("#66FFFFFF"), surface);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#66FFFFFF"), panel);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#EFFFFFFF"), fullyOpaqueProfile);
    }

    [Fact]
    public void SetBoxOpacity_ClampsUnsafeValues()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.SetBoxOpacity(AppTheme.Moe, -1);
            Assert.Equal(
                AppThemeManager.MinimumBoxOpacity,
                AppThemeManager.GetBoxOpacity(AppTheme.Moe),
                3);

            AppThemeManager.SetBoxOpacity(AppTheme.Moe, 5);
            Assert.Equal(
                AppThemeManager.MaximumBoxOpacity,
                AppThemeManager.GetBoxOpacity(AppTheme.Moe),
                3);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }
}
