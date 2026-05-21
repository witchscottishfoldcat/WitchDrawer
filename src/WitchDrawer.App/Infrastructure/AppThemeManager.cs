using System.Windows;
using System.Windows.Media;

namespace WitchDrawer.App.Infrastructure;

public static class AppThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Moe;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static void Apply(AppTheme theme)
    {
        _currentTheme = theme;

        if (theme == AppTheme.Glass)
        {
            SetColor("AppBackgroundBrush", "#DDF4F6FA");
            SetColor("PanelBrush", "#CCFFFFFF");
            SetColor("PanelAltBrush", "#88FFFFFF");
            SetColor("BorderBrushSoft", "#66FFFFFF");
            SetColor("TextPrimaryBrush", "#1D1D1F");
            SetColor("TextMutedBrush", "#6E7480");
            SetColor("AccentBrush", "#0A84FF");
            SetColor("AccentSoftBrush", "#4D0A84FF");
            SetColor("GlassSurfaceBrush", "#A6FFFFFF");
            SetColor("GlassInnerBrush", "#4DFFFFFF");
            SetColor("GlassStrokeBrush", "#8CFFFFFF");
            SetColor("PositiveBrush", "#30D158");
            SetColor("PositiveSoftBrush", "#5530D158");
            SetColor("DangerBrush", "#FF453A");
            SetColor("DangerSoftBrush", "#33FF453A");
            SetColor("HoverBrush", "#5CFFFFFF");
            SetColor("CardShadowBrush", "#00111827");
            SetColor("DropZoneBrush", "#50FFFFFF");
            SetColor("WindowOverlayBrush", "#00FFFFFF");
        }
        else
        {
            SetColor("AppBackgroundBrush", "#F5F5F7");
            SetColor("PanelBrush", "#FFFFFF");
            SetColor("PanelAltBrush", "#FAFAFC");
            SetColor("BorderBrushSoft", "#E5E5EA");
            SetColor("TextPrimaryBrush", "#1D1D1F");
            SetColor("TextMutedBrush", "#86868B");
            SetColor("AccentBrush", "#007AFF");
            SetColor("AccentSoftBrush", "#EAF3FF");
            SetColor("GlassSurfaceBrush", "#FFFFFF");
            SetColor("GlassInnerBrush", "#FAFAFC");
            SetColor("GlassStrokeBrush", "#E5E5EA");
            SetColor("PositiveBrush", "#34C759");
            SetColor("PositiveSoftBrush", "#E9F8EE");
            SetColor("DangerBrush", "#FF3B30");
            SetColor("DangerSoftBrush", "#FFF1F0");
            SetColor("HoverBrush", "#F2F2F7");
            SetColor("CardShadowBrush", "#001D1D1F");
            SetColor("DropZoneBrush", "#FAFAFC");
            SetColor("WindowOverlayBrush", "#00FFFFFF");
        }

        ThemeChanged?.Invoke(null, theme);
    }

    public static void ApplyToWindow(Window window)
    {
        if (!window.AllowsTransparency)
        {
            WindowBackdropManager.Apply(window, _currentTheme);
        }

        if (window.AllowsTransparency)
        {
            window.Background = Brushes.Transparent;
        }
        else
        {
            window.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        }
    }

    private static void SetColor(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

}
