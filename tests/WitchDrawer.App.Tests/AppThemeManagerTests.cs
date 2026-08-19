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
    public void OpacityCurve_PreservesCrystalPresetAndMakesMaximumFullyOpaque()
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
        Assert.Equal((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), fullyOpaqueProfile);
    }

    [Theory]
    [InlineData("AppBackgroundBrush", "#EB1C1C1E")]
    [InlineData("PanelBrush", "#D92C2C2E")]
    [InlineData("PanelAltBrush", "#C4222224")]
    [InlineData("GlassSurfaceBrush", "#D12C2C2E")]
    public void LegacyGlassOpacity_ReproducesOldThemeColors(string key, string expectedColor)
    {
        var color = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Glass,
            key,
            AppThemeManager.GetLegacyBoxOpacity(AppTheme.Glass));

        Assert.Equal((Color)ColorConverter.ConvertFromString(expectedColor), color);
    }

    [Fact]
    public void MaximumGlassOpacity_IsActuallyOpaque()
    {
        var surface = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Glass,
            "GlassSurfaceBrush",
            AppThemeManager.MaximumBoxOpacity);

        Assert.Equal((Color)ColorConverter.ConvertFromString("#FF2C2C2E"), surface);
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
