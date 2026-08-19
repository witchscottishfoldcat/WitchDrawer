using System.Windows;
using System.Windows.Media;

namespace WitchDrawer.App.Infrastructure;

public sealed class ThemeBoxOpacityChangedEventArgs(AppTheme theme, double opacity) : EventArgs
{
    public AppTheme Theme { get; } = theme;

    public double Opacity { get; } = opacity;
}

public static class AppThemeManager
{
    public const double DefaultBoxOpacity = 0.40;
    public const double MinimumBoxOpacity = 0.10;
    public const double MaximumBoxOpacity = 1.00;
    private const double LegacyGlassBoxOpacity = 0.82;
    private const double LegacyCrystalBoxOpacity = 0.94;

    private static AppTheme _currentTheme = AppTheme.Moe;

    private static readonly Dictionary<AppTheme, double> BoxOpacities =
        Enum.GetValues<AppTheme>().ToDictionary(theme => theme, GetDefaultBoxOpacity);

    private static readonly IReadOnlyDictionary<AppTheme, IReadOnlyDictionary<string, string>> ThemeColors =
        new Dictionary<AppTheme, IReadOnlyDictionary<string, string>>
        {
            [AppTheme.Moe] = new Dictionary<string, string>
            {
                ["ControlCenterSurfaceBrush"] = "#FFFBFBFD",
                ["AppBackgroundBrush"] = "#F5F5F7",
                ["PanelBrush"] = "#FFFFFF",
                ["PanelAltBrush"] = "#F2F2F7",
                ["BorderBrushSoft"] = "#D9D9DE",
                ["TextPrimaryBrush"] = "#1D1D1F",
                ["TextMutedBrush"] = "#6E6E73",
                ["AccentBrush"] = "#0071E3",
                ["AccentHoverBrush"] = "#0068D1",
                ["AccentPressedBrush"] = "#0059B3",
                ["AccentSoftBrush"] = "#E7F1FF",
                ["GlassSurfaceBrush"] = "#FFFFFF",
                ["DrawerSecondarySurfaceBrush"] = "#F2FFFFFF",
                ["GlassInnerBrush"] = "#F5F5F7",
                ["GlassStrokeBrush"] = "#E5E5EA",
                ["PositiveBrush"] = "#34C759",
                ["PositiveSoftBrush"] = "#EAF8EE",
                ["DangerBrush"] = "#FF3B30",
                ["DangerSoftBrush"] = "#FFF0EF",
                ["HoverBrush"] = "#EBEBF0",
                ["CardShadowBrush"] = "#12000000",
                ["DropZoneBrush"] = "#F7F7F9",
                ["WindowOverlayBrush"] = "#00FFFFFF"
            },
            [AppTheme.Glass] = new Dictionary<string, string>
            {
                ["ControlCenterSurfaceBrush"] = "#F21C1C1E",
                ["AppBackgroundBrush"] = "#EB1C1C1E",
                ["PanelBrush"] = "#D92C2C2E",
                ["PanelAltBrush"] = "#C4222224",
                ["BorderBrushSoft"] = "#33FFFFFF",
                ["TextPrimaryBrush"] = "#F5F5F7",
                ["TextMutedBrush"] = "#AEAEB2",
                ["AccentBrush"] = "#0A84FF",
                ["AccentHoverBrush"] = "#409CFF",
                ["AccentPressedBrush"] = "#0071E3",
                ["AccentSoftBrush"] = "#330A84FF",
                ["GlassSurfaceBrush"] = "#D12C2C2E",
                ["DrawerSecondarySurfaceBrush"] = "#8C121218",
                ["GlassInnerBrush"] = "#1FFFFFFF",
                ["GlassStrokeBrush"] = "#33FFFFFF",
                ["PositiveBrush"] = "#30D158",
                ["PositiveSoftBrush"] = "#2630D158",
                ["DangerBrush"] = "#FF453A",
                ["DangerSoftBrush"] = "#26FF453A",
                ["HoverBrush"] = "#2E3A3A3C",
                ["CardShadowBrush"] = "#52000000",
                ["DropZoneBrush"] = "#263A3A3C",
                ["WindowOverlayBrush"] = "#52000000"
            },
            [AppTheme.Crystal] = new Dictionary<string, string>
            {
                ["ControlCenterSurfaceBrush"] = "#F5F7F7FA",
                ["AppBackgroundBrush"] = "#EEF5F5F7",
                ["PanelBrush"] = "#F2FFFFFF",
                ["PanelAltBrush"] = "#E8F2F2F7",
                ["BorderBrushSoft"] = "#8FD9D9DE",
                ["TextPrimaryBrush"] = "#1D1D1F",
                ["TextMutedBrush"] = "#6E6E73",
                ["AccentBrush"] = "#0071E3",
                ["AccentHoverBrush"] = "#0068D1",
                ["AccentPressedBrush"] = "#0059B3",
                ["AccentSoftBrush"] = "#300071E3",
                ["GlassSurfaceBrush"] = "#EFFFFFFF",
                ["DrawerSecondarySurfaceBrush"] = "#C7F5F5F7",
                ["GlassInnerBrush"] = "#B8FFFFFF",
                ["GlassStrokeBrush"] = "#A6FFFFFF",
                ["PositiveBrush"] = "#34C759",
                ["PositiveSoftBrush"] = "#2634C759",
                ["DangerBrush"] = "#FF3B30",
                ["DangerSoftBrush"] = "#26FF3B30",
                ["HoverBrush"] = "#B8FFFFFF",
                ["CardShadowBrush"] = "#18000000",
                ["DropZoneBrush"] = "#B8FFFFFF",
                ["WindowOverlayBrush"] = "#38FFFFFF"
            }
        };

    // 旧版“全透水晶 · 透明盒”的实际颜色，作为 40% 不透明度的外壳基准。
    private static readonly IReadOnlyDictionary<string, string> LegacyTransparentCrystalBoxColors =
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

    private static readonly HashSet<string> OpacityAdjustedResourceKeys =
    [
        "AppBackgroundBrush",
        "PanelBrush",
        "PanelAltBrush",
        "BorderBrushSoft",
        "GlassSurfaceBrush",
        "DrawerSecondarySurfaceBrush",
        "HoverBrush",
        "DropZoneBrush",
        "WindowOverlayBrush"
    ];

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static event EventHandler<ThemeBoxOpacityChangedEventArgs>? BoxOpacityChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static void Apply(AppTheme theme)
    {
        _currentTheme = theme;

        foreach (var (key, color) in ThemeColors[theme])
        {
            SetColor(key, color);
        }

        ThemeChanged?.Invoke(null, theme);
    }

    public static double GetBoxOpacity(AppTheme theme)
    {
        return BoxOpacities[theme];
    }

    public static void SetBoxOpacity(AppTheme theme, double opacity)
    {
        var normalized = NormalizeOpacity(opacity);
        if (Math.Abs(BoxOpacities[theme] - normalized) < 0.0001)
        {
            return;
        }

        BoxOpacities[theme] = normalized;
        BoxOpacityChanged?.Invoke(null, new ThemeBoxOpacityChangedEventArgs(theme, normalized));
    }

    public static void ApplyDesktopBoxResources(ResourceDictionary resources)
    {
        foreach (var key in LegacyTransparentCrystalBoxColors.Keys)
        {
            resources.Remove(key);
        }

        var opacity = GetBoxOpacity(_currentTheme);
        foreach (var key in LegacyTransparentCrystalBoxColors.Keys)
        {
            var color = GetDesktopBoxColor(_currentTheme, key, opacity);
            if (color == ParseColor(ThemeColors[_currentTheme][key]))
            {
                continue;
            }

            var brush = new SolidColorBrush(color);
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

        window.Background = window.AllowsTransparency
            ? Brushes.Transparent
            : (Brush)Application.Current.Resources["AppBackgroundBrush"];
    }

    internal static Color GetDesktopBoxColor(AppTheme theme, string key, double opacity)
    {
        var baseColor = ParseColor(ThemeColors[theme][key]);
        var normalized = NormalizeOpacity(opacity);

        if (!OpacityAdjustedResourceKeys.Contains(key))
        {
            if (theme != AppTheme.Crystal)
            {
                return baseColor;
            }

            var legacyColor = ParseColor(LegacyTransparentCrystalBoxColors[key]);
            if (normalized <= DefaultBoxOpacity)
            {
                return legacyColor;
            }

            var crystalLegacyOpacity = GetLegacyBoxOpacity(theme);
            return normalized >= crystalLegacyOpacity
                ? baseColor
                : Interpolate(
                    legacyColor,
                    baseColor,
                    ScaleBetween(normalized, DefaultBoxOpacity, crystalLegacyOpacity));
        }

        var transparentColor = theme == AppTheme.Crystal
            ? ParseColor(LegacyTransparentCrystalBoxColors[key])
            : CreateEquivalentTransparentColor(key, baseColor);

        if (normalized <= DefaultBoxOpacity)
        {
            var alphaScale = normalized / DefaultBoxOpacity;
            return Color.FromArgb(
                ToByte(transparentColor.A * alphaScale),
                transparentColor.R,
                transparentColor.G,
                transparentColor.B);
        }

        var legacyOpacity = GetLegacyBoxOpacity(theme);
        if (normalized <= legacyOpacity)
        {
            return Interpolate(
                transparentColor,
                baseColor,
                ScaleBetween(normalized, DefaultBoxOpacity, legacyOpacity));
        }

        var opaqueColor = Color.FromArgb(byte.MaxValue, baseColor.R, baseColor.G, baseColor.B);
        return Interpolate(
            baseColor,
            opaqueColor,
            ScaleBetween(normalized, legacyOpacity, MaximumBoxOpacity));
    }

    internal static double GetLegacyBoxOpacity(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Glass => LegacyGlassBoxOpacity,
            AppTheme.Crystal => LegacyCrystalBoxOpacity,
            _ => MaximumBoxOpacity
        };
    }

    internal static double GetDefaultBoxOpacity(AppTheme theme)
    {
        return theme == AppTheme.Crystal
            ? DefaultBoxOpacity
            : GetLegacyBoxOpacity(theme);
    }

    internal static void ResetBoxOpacitiesForTests(double? opacity = null)
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            BoxOpacities[theme] = opacity is null
                ? GetDefaultBoxOpacity(theme)
                : NormalizeOpacity(opacity.Value);
        }
    }

    private static Color CreateEquivalentTransparentColor(string key, Color baseColor)
    {
        var crystalBaseColor = ParseColor(ThemeColors[AppTheme.Crystal][key]);
        var crystalTransparentColor = ParseColor(LegacyTransparentCrystalBoxColors[key]);
        var alphaRatio = crystalBaseColor.A == 0
            ? 0
            : (double)crystalTransparentColor.A / crystalBaseColor.A;

        return Color.FromArgb(
            ToByte(baseColor.A * alphaRatio),
            baseColor.R,
            baseColor.G,
            baseColor.B);
    }

    private static double ScaleBetween(double value, double lowerBound, double upperBound)
    {
        if (Math.Abs(upperBound - lowerBound) < 0.0001)
        {
            return 1;
        }

        return (value - lowerBound) / (upperBound - lowerBound);
    }

    private static Color Interpolate(Color from, Color to, double progress)
    {
        return Color.FromArgb(
            ToByte(from.A + ((to.A - from.A) * progress)),
            ToByte(from.R + ((to.R - from.R) * progress)),
            ToByte(from.G + ((to.G - from.G) * progress)),
            ToByte(from.B + ((to.B - from.B) * progress)));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), byte.MinValue, byte.MaxValue);
    }

    private static double NormalizeOpacity(double opacity)
    {
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
        {
            return DefaultBoxOpacity;
        }

        return Math.Clamp(opacity, MinimumBoxOpacity, MaximumBoxOpacity);
    }

    private static Color ParseColor(string color)
    {
        return (Color)ColorConverter.ConvertFromString(color);
    }

    private static void SetColor(string key, string color)
    {
        var brush = new SolidColorBrush(ParseColor(color));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
