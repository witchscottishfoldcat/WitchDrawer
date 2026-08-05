using System.Windows;
using System.Windows.Media;

namespace WitchDrawer.App.Infrastructure;

public static class AppThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Moe;
    private static bool _useTransparentCrystalBoxes;

    private static readonly IReadOnlyDictionary<string, string> TransparentCrystalBoxColors =
        new Dictionary<string, string>
        {
            ["AppBackgroundBrush"] = "#78FFFFFF",
            ["PanelBrush"] = "#66FFFFFF",
            ["PanelAltBrush"] = "#4DF2F2F7",
            ["BorderBrushSoft"] = "#59FFFFFF",
            ["TextPrimaryBrush"] = "#1D1D1F",
            ["TextMutedBrush"] = "#6E6E73",
            ["AccentBrush"] = "#0071E3",
            ["AccentSoftBrush"] = "#2E0071E3",
            ["GlassSurfaceBrush"] = "#66FFFFFF",
            ["DrawerSecondarySurfaceBrush"] = "#70F5F5F7",
            ["GlassInnerBrush"] = "#3DFFFFFF",
            ["GlassStrokeBrush"] = "#66FFFFFF",
            ["PositiveBrush"] = "#34C759",
            ["PositiveSoftBrush"] = "#2634C759",
            ["DangerBrush"] = "#FF3B30",
            ["DangerSoftBrush"] = "#26FF3B30",
            ["HoverBrush"] = "#52FFFFFF",
            ["CardShadowBrush"] = "#18000000",
            ["DropZoneBrush"] = "#33FFFFFF",
            ["WindowOverlayBrush"] = "#24FFFFFF"
        };

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static event EventHandler<bool>? CrystalBoxTransparencyChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static bool UseTransparentCrystalBoxes => _useTransparentCrystalBoxes;

    public static void Apply(AppTheme theme)
    {
        _currentTheme = theme;

        if (theme == AppTheme.Glass)
        {
            SetColor("ControlCenterSurfaceBrush", "#F21C1C1E");
            SetColor("AppBackgroundBrush", "#EB1C1C1E");
            SetColor("PanelBrush", "#D92C2C2E");
            SetColor("PanelAltBrush", "#C4222224");
            SetColor("BorderBrushSoft", "#33FFFFFF");
            SetColor("TextPrimaryBrush", "#F5F5F7");
            SetColor("TextMutedBrush", "#AEAEB2");
            SetColor("AccentBrush", "#0A84FF");
            SetColor("AccentHoverBrush", "#409CFF");
            SetColor("AccentPressedBrush", "#0071E3");
            SetColor("AccentSoftBrush", "#330A84FF");
            SetColor("GlassSurfaceBrush", "#D12C2C2E");
            SetColor("DrawerSecondarySurfaceBrush", "#8C121218");
            SetColor("GlassInnerBrush", "#1FFFFFFF");
            SetColor("GlassStrokeBrush", "#33FFFFFF");
            SetColor("PositiveBrush", "#30D158");
            SetColor("PositiveSoftBrush", "#2630D158");
            SetColor("DangerBrush", "#FF453A");
            SetColor("DangerSoftBrush", "#26FF453A");
            SetColor("HoverBrush", "#2E3A3A3C");
            SetColor("CardShadowBrush", "#52000000");
            SetColor("DropZoneBrush", "#263A3A3C");
            SetColor("WindowOverlayBrush", "#52000000");
        }
        else if (theme == AppTheme.Crystal)
        {
            SetColor("ControlCenterSurfaceBrush", "#F5F7F7FA");
            SetColor("AppBackgroundBrush", "#EEF5F5F7");
            SetColor("PanelBrush", "#F2FFFFFF");
            SetColor("PanelAltBrush", "#E8F2F2F7");
            SetColor("BorderBrushSoft", "#8FD9D9DE");
            SetColor("TextPrimaryBrush", "#1D1D1F");
            SetColor("TextMutedBrush", "#6E6E73");
            SetColor("AccentBrush", "#0071E3");
            SetColor("AccentHoverBrush", "#0068D1");
            SetColor("AccentPressedBrush", "#0059B3");
            SetColor("AccentSoftBrush", "#300071E3");
            SetColor("GlassSurfaceBrush", "#EFFFFFFF");
            SetColor("DrawerSecondarySurfaceBrush", "#C7F5F5F7");
            SetColor("GlassInnerBrush", "#B8FFFFFF");
            SetColor("GlassStrokeBrush", "#A6FFFFFF");
            SetColor("PositiveBrush", "#34C759");
            SetColor("PositiveSoftBrush", "#2634C759");
            SetColor("DangerBrush", "#FF3B30");
            SetColor("DangerSoftBrush", "#26FF3B30");
            SetColor("HoverBrush", "#B8FFFFFF");
            SetColor("CardShadowBrush", "#18000000");
            SetColor("DropZoneBrush", "#B8FFFFFF");
            SetColor("WindowOverlayBrush", "#38FFFFFF");
        }
        else
        {
            SetColor("ControlCenterSurfaceBrush", "#FFFBFBFD");
            SetColor("AppBackgroundBrush", "#F5F5F7");
            SetColor("PanelBrush", "#FFFFFF");
            SetColor("PanelAltBrush", "#F2F2F7");
            SetColor("BorderBrushSoft", "#D9D9DE");
            SetColor("TextPrimaryBrush", "#1D1D1F");
            SetColor("TextMutedBrush", "#6E6E73");
            SetColor("AccentBrush", "#0071E3");
            SetColor("AccentHoverBrush", "#0068D1");
            SetColor("AccentPressedBrush", "#0059B3");
            SetColor("AccentSoftBrush", "#E7F1FF");
            SetColor("GlassSurfaceBrush", "#FFFFFF");
            SetColor("DrawerSecondarySurfaceBrush", "#F2FFFFFF");
            SetColor("GlassInnerBrush", "#F5F5F7");
            SetColor("GlassStrokeBrush", "#E5E5EA");
            SetColor("PositiveBrush", "#34C759");
            SetColor("PositiveSoftBrush", "#EAF8EE");
            SetColor("DangerBrush", "#FF3B30");
            SetColor("DangerSoftBrush", "#FFF0EF");
            SetColor("HoverBrush", "#EBEBF0");
            SetColor("CardShadowBrush", "#12000000");
            SetColor("DropZoneBrush", "#F7F7F9");
            SetColor("WindowOverlayBrush", "#00FFFFFF");
        }

        ThemeChanged?.Invoke(null, theme);
    }

    public static void SetCrystalBoxTransparency(bool enabled)
    {
        if (_useTransparentCrystalBoxes == enabled)
        {
            return;
        }

        _useTransparentCrystalBoxes = enabled;
        CrystalBoxTransparencyChanged?.Invoke(null, enabled);
    }

    public static void ApplyDesktopBoxResources(ResourceDictionary resources)
    {
        foreach (var key in TransparentCrystalBoxColors.Keys)
        {
            resources.Remove(key);
        }

        if (_currentTheme != AppTheme.Crystal || !_useTransparentCrystalBoxes)
        {
            return;
        }

        foreach (var (key, color) in TransparentCrystalBoxColors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            resources[key] = brush;
        }
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
