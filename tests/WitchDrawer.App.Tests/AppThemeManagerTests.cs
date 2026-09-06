using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class AppThemeManagerTests
{
    [Fact]
    public void SetBoxOpacity_RaisesEventOnlyWhenValueChanges()
    {
        var theme = AppTheme.Crystal;
        AppThemeManager.SetBoxOpacity(theme, AppThemeManager.DefaultBoxOpacity);
        var changes = new List<double>();
        EventHandler<ThemeBoxOpacityChangedEventArgs> handler = (_, e) => changes.Add(e.Opacity);
        AppThemeManager.BoxOpacityChanged += handler;

        try
        {
            AppThemeManager.SetBoxOpacity(theme, 0.5);
            AppThemeManager.SetBoxOpacity(theme, 0.5);
            AppThemeManager.SetBoxOpacity(theme, 0.8);

            Assert.Equal([0.5, 0.8], changes);
        }
        finally
        {
            AppThemeManager.BoxOpacityChanged -= handler;
            AppThemeManager.SetBoxOpacity(theme, AppThemeManager.DefaultBoxOpacity);
        }
    }
}
