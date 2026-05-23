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
            SetColor("AppBackgroundBrush", "#E60D0D16"); // Deep obsidian dark translucent
            SetColor("PanelBrush", "#CC1A1A24");         // Dark card/panel background
            SetColor("PanelAltBrush", "#9914141E");      // Deep sidebar/control background
            SetColor("BorderBrushSoft", "#26FFFFFF");     // Translucent border for glassmorphism
            SetColor("TextPrimaryBrush", "#F3F4F6");     // High contrast silver-white text
            SetColor("TextMutedBrush", "#9CA3AF");       // Muted silver text
            SetColor("AccentBrush", "#3B82F6");          // Clear blue accent
            SetColor("AccentSoftBrush", "#333B82F6");     // Translucent selection highlight
            SetColor("GlassSurfaceBrush", "#B3121218");  // Floating desktop box surface
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
            SetColor("AppBackgroundBrush", "#66FFFFFF");  // High transparent crystal white
            SetColor("PanelBrush", "#4DFFFFFF");          // Very translucent card panel
            SetColor("PanelAltBrush", "#33FFFFFF");       // Ultra translucent sidebar
            SetColor("BorderBrushSoft", "#40FFFFFF");     // Frosted glass border
            SetColor("TextPrimaryBrush", "#111827");      // Dark slate text
            SetColor("TextMutedBrush", "#4B5563");        // Muted slate text
            SetColor("AccentBrush", "#0EA5E9");           // Clear Sky Blue
            SetColor("AccentSoftBrush", "#330EA5E9");     // Translucent Sky Blue tint
            SetColor("GlassSurfaceBrush", "#4DFFFFFF");   // Very light floating surface
            SetColor("GlassInnerBrush", "#26FFFFFF");     // Barely visible inner glass
            SetColor("GlassStrokeBrush", "#66FFFFFF");    // Pronounced crystal outline
            SetColor("PositiveBrush", "#10B981");         // Emerald green
            SetColor("PositiveSoftBrush", "#3310B981");   // Translucent green
            SetColor("DangerBrush", "#EF4444");           // Rose red
            SetColor("DangerSoftBrush", "#33EF4444");     // Translucent red
            SetColor("HoverBrush", "#40FFFFFF");          // Crystal light glare hover
            SetColor("CardShadowBrush", "#1A000000");     // Very soft shadow
            SetColor("DropZoneBrush", "#1AFFFFFF");       // Translucent drop-zone
            SetColor("WindowOverlayBrush", "#33FFFFFF");  // Light dimming overlay
        }
        else
        {
            SetColor("AppBackgroundBrush", "#F3F4F6");    // Clean soft light background
            SetColor("PanelBrush", "#FFFFFF");            // White card/panel background
            SetColor("PanelAltBrush", "#F9FAFB");         // Light gray sidebar background
            SetColor("BorderBrushSoft", "#E5E7EB");       // Soft light gray border
            SetColor("TextPrimaryBrush", "#111827");      // Slate primary text
            SetColor("TextMutedBrush", "#6B7280");        // Slate muted text
            SetColor("AccentBrush", "#007AFF");           // Clean system blue
            SetColor("AccentSoftBrush", "#EAF3FF");        // Soft blue tint
            SetColor("GlassSurfaceBrush", "#FBFBFD");     // Light floating box surface
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
