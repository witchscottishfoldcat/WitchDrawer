using System.Windows;
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
    public void GlassStrokeColorCurve_PreservesLegacyOpacityBehavior()
    {
        var moeStroke = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Moe,
            "GlassStrokeBrush",
            AppThemeManager.MinimumBoxOpacity);
        var glassStroke = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Glass,
            "GlassStrokeBrush",
            AppThemeManager.MinimumBoxOpacity);
        var crystalTransparentStroke = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "GlassStrokeBrush",
            AppThemeManager.MinimumBoxOpacity);
        var crystalOpaqueStroke = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "GlassStrokeBrush",
            AppThemeManager.MaximumBoxOpacity);

        Assert.Equal(byte.MaxValue, moeStroke.A);
        Assert.Equal(0x33, glassStroke.A);
        Assert.Equal(0x66, crystalTransparentStroke.A);
        Assert.Equal(0xA6, crystalOpaqueStroke.A);
    }

    [Fact]
    public void DesktopChromeDefaults_ReproduceEachThemesOriginalVisuals()
    {
        Assert.Equal(1.00, AppThemeManager.GetDefaultBoxBorderOpacity(AppTheme.Moe), 3);
        Assert.Equal(0.20, AppThemeManager.GetDefaultBoxBorderOpacity(AppTheme.Glass), 3);
        Assert.Equal(0.40, AppThemeManager.GetDefaultBoxBorderOpacity(AppTheme.Crystal), 3);
        Assert.Equal(1.00, AppThemeManager.GetDefaultIconFrameOpacity(AppTheme.Moe), 3);
        Assert.Equal(0x1F / 255d, AppThemeManager.GetDefaultIconFrameOpacity(AppTheme.Glass), 3);
        Assert.Equal(0x3D / 255d, AppThemeManager.GetDefaultIconFrameOpacity(AppTheme.Crystal), 3);
    }

    [Fact]
    public void DesktopBoxChromeOpacity_IsIndependentFromBoxSurfaceOpacity()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var changes = new List<AppTheme>();
        EventHandler<ThemeDesktopBoxChromeChangedEventArgs> handler =
            (_, change) => changes.Add(change.Theme);
        AppThemeManager.DesktopBoxChromeChanged += handler;

        try
        {
            AppThemeManager.SetBoxBorderOpacity(AppTheme.Crystal, 0.75);
            AppThemeManager.SetIconFrameOpacity(AppTheme.Crystal, 0.25);
            AppThemeManager.SetBoxOpacity(AppTheme.Crystal, AppThemeManager.MinimumBoxOpacity);

            var transparentSurfaceBorder = AppThemeManager.GetDesktopBoxBorderColor(AppTheme.Crystal);
            var transparentSurfaceIconFrame = AppThemeManager.GetDesktopIconFrameColor(AppTheme.Crystal);

            AppThemeManager.SetBoxOpacity(AppTheme.Crystal, AppThemeManager.MaximumBoxOpacity);

            var opaqueSurfaceBorder = AppThemeManager.GetDesktopBoxBorderColor(AppTheme.Crystal);
            var opaqueSurfaceIconFrame = AppThemeManager.GetDesktopIconFrameColor(AppTheme.Crystal);

            Assert.Equal(191, transparentSurfaceBorder.A);
            Assert.Equal(191, opaqueSurfaceBorder.A);
            Assert.Equal(64, transparentSurfaceIconFrame.A);
            Assert.Equal(64, opaqueSurfaceIconFrame.A);
            Assert.Equal([AppTheme.Crystal, AppTheme.Crystal], changes);
        }
        finally
        {
            AppThemeManager.DesktopBoxChromeChanged -= handler;
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public void DesktopBoxChromeOpacity_ClampsFullRangeAndRejectsNonFiniteValues()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.SetBoxBorderOpacity(AppTheme.Moe, -1);
            AppThemeManager.SetIconFrameOpacity(AppTheme.Moe, 2);

            Assert.Equal(0, AppThemeManager.GetBoxBorderOpacity(AppTheme.Moe));
            Assert.Equal(1, AppThemeManager.GetIconFrameOpacity(AppTheme.Moe));

            AppThemeManager.SetBoxBorderOpacity(AppTheme.Moe, double.NaN);
            AppThemeManager.SetIconFrameOpacity(AppTheme.Moe, double.PositiveInfinity);

            Assert.Equal(0, AppThemeManager.GetBoxBorderOpacity(AppTheme.Moe));
            Assert.Equal(1, AppThemeManager.GetIconFrameOpacity(AppTheme.Moe));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public void IconFrameFillAndBorder_AlwaysShareTheConfiguredOpacity()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.SetIconFrameOpacity(AppTheme.Crystal, 0);

            var transparentFill = AppThemeManager.GetDesktopIconFrameColor(AppTheme.Crystal);
            var transparentBorder = AppThemeManager.GetDesktopIconFrameBorderColor(AppTheme.Crystal);

            Assert.Equal(0, transparentFill.A);
            Assert.Equal(transparentFill.A, transparentBorder.A);

            AppThemeManager.SetIconFrameOpacity(AppTheme.Crystal, 1);

            var opaqueFill = AppThemeManager.GetDesktopIconFrameColor(AppTheme.Crystal);
            var opaqueBorder = AppThemeManager.GetDesktopIconFrameBorderColor(AppTheme.Crystal);

            Assert.Equal(byte.MaxValue, opaqueFill.A);
            Assert.Equal(opaqueFill.A, opaqueBorder.A);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public void EditorSurfaceOpacity_PreservesItsOwnColorAndBecomesFullyOpaqueAtMaximum()
    {
        var transparentSurface = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "ControlCenterSurfaceBrush",
            AppThemeManager.DefaultBoxOpacity);
        var opaqueSurface = AppThemeManager.GetDesktopBoxColor(
            AppTheme.Crystal,
            "ControlCenterSurfaceBrush",
            AppThemeManager.MaximumBoxOpacity);

        Assert.Equal((Color)ColorConverter.ConvertFromString("#7BF7F7FA"), transparentSurface);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#FFF7F7FA"), opaqueSurface);
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

    [Fact]
    public void ClearEditorOpacityResources_RemovesOnlyEditorThemeOverrides()
    {
        var resources = new ResourceDictionary
        {
            ["ControlCenterSurfaceBrush"] = Brushes.Transparent,
            ["PanelBrush"] = Brushes.Transparent,
            ["TextPrimaryBrush"] = Brushes.Black,
            ["UnrelatedResource"] = "keep"
        };

        AppThemeManager.ClearEditorOpacityResources(resources);

        Assert.False(resources.Contains("ControlCenterSurfaceBrush"));
        Assert.False(resources.Contains("PanelBrush"));
        Assert.False(resources.Contains("TextPrimaryBrush"));
        Assert.Equal("keep", resources["UnrelatedResource"]);
    }
}
