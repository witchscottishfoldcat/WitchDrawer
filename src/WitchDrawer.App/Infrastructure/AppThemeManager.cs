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
            ["AppBackgroundBrush"] = "#66FFFFFF",
            ["PanelBrush"] = "#4DFFFFFF",
            ["PanelAltBrush"] = "#33FFFFFF",
            ["BorderBrushSoft"] = "#40FFFFFF",
            ["TextPrimaryBrush"] = "#111827",
            ["TextMutedBrush"] = "#4B5563",
            ["AccentBrush"] = "#0EA5E9",
            ["AccentSoftBrush"] = "#330EA5E9",
            ["GlassSurfaceBrush"] = "#4DFFFFFF",
            ["DrawerSecondarySurfaceBrush"] = "#5CF4FAFF",
            ["GlassInnerBrush"] = "#26FFFFFF",
            ["GlassStrokeBrush"] = "#66FFFFFF",
            ["PositiveBrush"] = "#10B981",
            ["PositiveSoftBrush"] = "#3310B981",
            ["DangerBrush"] = "#EF4444",
            ["DangerSoftBrush"] = "#33EF4444",
            ["HoverBrush"] = "#40FFFFFF",
            ["CardShadowBrush"] = "#1A000000",
            ["DropZoneBrush"] = "#1AFFFFFF",
            ["WindowOverlayBrush"] = "#33FFFFFF"
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
            SetColor("ControlCenterSurfaceBrush", "#F20D0D16"); // Stable dark control-center surface
            SetColor("AppBackgroundBrush", "#E60D0D16"); // Deep obsidian dark translucent
            SetColor("PanelBrush", "#CC1A1A24");         // Dark card/panel background
            SetColor("PanelAltBrush", "#9914141E");      // Deep sidebar/control background
            SetColor("BorderBrushSoft", "#26FFFFFF");     // Translucent border for glassmorphism
            SetColor("TextPrimaryBrush", "#F3F4F6");     // High contrast silver-white text
            SetColor("TextMutedBrush", "#9CA3AF");       // Muted silver text
            SetColor("AccentBrush", "#3B82F6");          // Clear blue accent
            SetColor("AccentHoverBrush", "#2B6FE0");     // Deeper accent on hover (opaque)
            SetColor("AccentPressedBrush", "#1F5CC9");   // Deepest accent on press (opaque)
            SetColor("AccentSoftBrush", "#333B82F6");     // Translucent selection highlight
            SetColor("GlassSurfaceBrush", "#B3121218");  // Floating desktop box surface
            SetColor("DrawerSecondarySurfaceBrush", "#8C121218");
            SetColor("GlassInnerBrush", "#1AFFFFFF");     // File icon container backplate
            SetColor("GlassStrokeBrush", "#26FFFFFF");    // Thin desktop box outline
            SetColor("PositiveBrush", "#10B981");         // Glowing emerald green
            SetColor("PositiveSoftBrush", "#2610B981");   // Translucent positive feedback
            SetColor("DangerBrush", "#EF4444");           // Glowing rose red
            SetColor("DangerSoftBrush", "#26EF4444");     // Translucent danger feedback
            SetColor("HoverBrush", "#1FFFFFFF");          // Light glare hover on glass
            SetColor("CardShadowBrush", "#4D000000");     // Shadow overlay
            SetColor("DropZoneBrush", "#0DFFFFFF");       // Very thin white drop-zone
            SetColor("WindowOverlayBrush", "#40000000");  // Dimming overlay
        }
        else if (theme == AppTheme.Crystal)
        {
            SetColor("ControlCenterSurfaceBrush", "#EDF3F8FC"); // Quiet readable surface over wallpapers
            SetColor("AppBackgroundBrush", "#B8F3F8FC");  // Readable crystal veil over wallpapers
            SetColor("PanelBrush", "#B8FFFFFF");          // Stable content surface
            SetColor("PanelAltBrush", "#94F7FAFC");       // Distinct but lightweight sidebar
            SetColor("BorderBrushSoft", "#A6FFFFFF");     // Crisp crystal edge
            SetColor("TextPrimaryBrush", "#111827");      // Dark slate text
            SetColor("TextMutedBrush", "#4B5563");        // Muted slate text
            SetColor("AccentBrush", "#0EA5E9");           // Clear Sky Blue
            SetColor("AccentHoverBrush", "#0B8AC7");     // Deeper sky blue on hover (opaque)
            SetColor("AccentPressedBrush", "#0977AE");   // Deepest sky blue on press (opaque)
            SetColor("AccentSoftBrush", "#400EA5E9");     // Translucent Sky Blue tint
            SetColor("GlassSurfaceBrush", "#A6FFFFFF");   // Readable floating surface
            SetColor("DrawerSecondarySurfaceBrush", "#70F4FAFF");
            SetColor("GlassInnerBrush", "#73FFFFFF");     // Quiet inner glass
            SetColor("GlassStrokeBrush", "#A6FFFFFF");    // Pronounced crystal outline
            SetColor("PositiveBrush", "#10B981");         // Emerald green
            SetColor("PositiveSoftBrush", "#3310B981");   // Translucent green
            SetColor("DangerBrush", "#EF4444");           // Rose red
            SetColor("DangerSoftBrush", "#33EF4444");     // Translucent red
            SetColor("HoverBrush", "#80FFFFFF");          // Crystal light glare hover
            SetColor("CardShadowBrush", "#1A000000");     // Very soft shadow
            SetColor("DropZoneBrush", "#66FFFFFF");       // Calm, readable drop-zone
            SetColor("WindowOverlayBrush", "#66FFFFFF");  // Light wallpaper veil
        }
        else
        {
            SetColor("ControlCenterSurfaceBrush", "#FFF5F8FC"); // Native cool-white workspace
            SetColor("AppBackgroundBrush", "#F3F4F6");    // Clean soft light background
            SetColor("PanelBrush", "#FFFFFF");            // White card/panel background
            SetColor("PanelAltBrush", "#F9FAFB");         // Light gray sidebar background
            SetColor("BorderBrushSoft", "#E5E7EB");       // Soft light gray border
            SetColor("TextPrimaryBrush", "#111827");      // Slate primary text
            SetColor("TextMutedBrush", "#6B7280");        // Slate muted text
            SetColor("AccentBrush", "#007AFF");           // Clean system blue
            SetColor("AccentHoverBrush", "#0066D6");     // Deeper system blue on hover (opaque)
            SetColor("AccentPressedBrush", "#0052B3");   // Deepest system blue on press (opaque)
            SetColor("AccentSoftBrush", "#EAF3FF");        // Soft blue tint
            SetColor("GlassSurfaceBrush", "#FBFBFD");     // Light floating box surface
            SetColor("DrawerSecondarySurfaceBrush", "#F2FBFBFD");
            SetColor("GlassInnerBrush", "#F3F4F6");       // Light icon container backplate
            SetColor("GlassStrokeBrush", "#E5E7EB");      // Light box outline
            SetColor("PositiveBrush", "#10B981");         // Emerald green
            SetColor("PositiveSoftBrush", "#ECFDF5");     // Soft green feedback
            SetColor("DangerBrush", "#EF4444");           // Rose red
            SetColor("DangerSoftBrush", "#FEF2F2");       // Soft red feedback
            SetColor("HoverBrush", "#F3F4F6");            // Light gray hover
            SetColor("CardShadowBrush", "#0A000000");     // Soft gray shadow
            SetColor("DropZoneBrush", "#F9FAFB");         // Soft gray drop-zone
            SetColor("WindowOverlayBrush", "#00FFFFFF");  // Transparent overlay
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
