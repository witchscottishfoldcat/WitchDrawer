using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class AppThemeManagerTests
{
    [Fact]
    public void SetCrystalBoxTransparency_RaisesEventOnlyWhenValueChanges()
    {
        AppThemeManager.SetCrystalBoxTransparency(false);
        var changes = new List<bool>();
        EventHandler<bool> handler = (_, enabled) => changes.Add(enabled);
        AppThemeManager.CrystalBoxTransparencyChanged += handler;

        try
        {
            AppThemeManager.SetCrystalBoxTransparency(true);
            AppThemeManager.SetCrystalBoxTransparency(true);
            AppThemeManager.SetCrystalBoxTransparency(false);

            Assert.Equal([true, false], changes);
        }
        finally
        {
            AppThemeManager.CrystalBoxTransparencyChanged -= handler;
            AppThemeManager.SetCrystalBoxTransparency(false);
        }
    }

    [Fact]
    public void SetBoxBackgroundOpacity_ClampsAndRaisesEventOnlyWhenValueChanges()
    {
        AppThemeManager.SetBoxBackgroundOpacity(1.0);
        var changes = new List<double>();
        EventHandler<double> handler = (_, opacity) => changes.Add(opacity);
        AppThemeManager.BoxBackgroundOpacityChanged += handler;

        try
        {
            AppThemeManager.SetBoxBackgroundOpacity(0.6);
            AppThemeManager.SetBoxBackgroundOpacity(0.6);
            AppThemeManager.SetBoxBackgroundOpacity(0.0); // 低于下限 → clamp 到 0.05
            AppThemeManager.SetBoxBackgroundOpacity(3.0); // 高于上限 → clamp 到 1.0

            Assert.Equal(1.0, AppThemeManager.BoxBackgroundOpacity, precision: 10);
            Assert.Equal([0.6, 0.05, 1.0], changes);
        }
        finally
        {
            AppThemeManager.BoxBackgroundOpacityChanged -= handler;
            AppThemeManager.SetBoxBackgroundOpacity(1.0);
        }
    }
}
