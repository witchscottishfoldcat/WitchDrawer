using System.Windows;
using System.Windows.Media;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Infrastructure;

public static partial class AppThemeManager
{
    public static DesktopBoxCornerSettings DesktopCorners { get; } = new();
    private static readonly Dictionary<AppTheme, ThemeCustomization> Customizations =
        Enum.GetValues<AppTheme>().ToDictionary(theme => theme, _ => ThemeCustomization.Empty);

    public static ThemeCustomization GetCustomization(AppTheme theme) => Customizations[theme];

    public static void SetCustomization(AppTheme theme, ThemeCustomization customization)
    {
        var normalized = customization.Normalize();
        var previous = Customizations[theme];
        if (previous == normalized) return;
        Customizations[theme] = normalized;
        if (theme == CurrentTheme && Application.Current is not null &&
            (previous.Accent != normalized.Accent || previous.ItemHover != normalized.ItemHover ||
             previous.ItemSelected != normalized.ItemSelected || previous.ItemHoverOpacity != normalized.ItemHoverOpacity))
            ApplySharedCustomization();
        DesktopBoxAppearanceChanged?.Invoke(null, theme);
    }

    private static void ApplySharedCustomization()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSoftBrush" })
            SetResourceColor(resources, key, ResolveAccentColor(CurrentTheme, key, ParseColor(ThemeColors[CurrentTheme][key])));
        SetResourceColor(resources, "ItemHoverBrush", GetItemHoverColor(CurrentTheme, desktop: false));
        SetResourceColor(resources, "ItemSelectedBrush", GetItemSelectedColor(CurrentTheme, desktop: false));
    }

    private static void ApplyDesktopCustomization(ResourceDictionary resources)
    {
        SetResourceColor(resources, "GlassSurfaceBrush", GetDesktopSurfaceColor(CurrentTheme));
        SetResourceColor(resources, "DrawerSecondarySurfaceBrush", WithRgb(GetOriginalColor(CurrentTheme, "DrawerSecondarySurfaceBrush"), GetCustomization(CurrentTheme).BoxBackground));
        SetDesktopInteractionColor(resources, "ItemHoverBrush", GetItemHoverColor(CurrentTheme, desktop: true));
        SetDesktopInteractionColor(resources, "ItemSelectedBrush", GetItemSelectedColor(CurrentTheme, desktop: true));
        DesktopCorners.Apply(GetCustomization(CurrentTheme));
    }

    private static void SetDesktopInteractionColor(ResourceDictionary resources, string key, Color color)
    {
        // Matching interaction colors can inherit the global frozen brush, so
        // one slider edit does not invalidate both the app and each box's resources.
        if (Application.Current is { } app && !ReferenceEquals(resources, app.Resources)
            && app.Resources[key] is SolidColorBrush shared && shared.Color == color)
        {
            if (resources.Contains(key)) resources.Remove(key);
            return;
        }
        SetResourceColor(resources, key, color);
    }

    internal static Color GetDesktopSurfaceColor(AppTheme theme) =>
        WithRgb(GetOriginalColor(theme, "GlassSurfaceBrush"), GetCustomization(theme).BoxBackground);

    internal static Color GetItemHoverColor(AppTheme theme, bool desktop)
    {
        var baseline = desktop ? GetOriginalColor(theme, "HoverBrush") : ParseColor(ThemeColors[theme]["HoverBrush"]);
        var settings = GetCustomization(theme);
        return WithRgb(AdjustAppearanceAlpha(baseline, baseline.A, settings.ItemHoverOpacity), settings.ItemHover);
    }

    internal static Color GetItemSelectedColor(AppTheme theme, bool desktop)
    {
        var baseline = desktop ? GetOriginalColor(theme, "AccentSoftBrush") : ParseColor(ThemeColors[theme]["AccentSoftBrush"]);
        return WithRgb(ResolveAccentColor(theme, "AccentSoftBrush", baseline), GetCustomization(theme).ItemSelected);
    }

    internal static Color ResolveAccentColor(AppTheme theme, string key, Color baseline)
    {
        if (GetCustomization(theme).Accent is not { } hex) return baseline;
        var accent = ParseColor(hex);
        return key switch
        {
            "AccentBrush" => accent,
            "AccentHoverBrush" => Interpolate(accent, theme == AppTheme.Glass ? Colors.White : Colors.Black, 0.12),
            "AccentPressedBrush" => Interpolate(accent, Colors.Black, 0.24),
            "AccentSoftBrush" => baseline.A == 255
                ? Interpolate(Colors.White, accent, 0.10)
                : WithRgb(baseline, hex),
            _ => baseline
        };
    }

    private static Color WithRgb(Color baseline, string? hex)
    {
        if (hex is null) return baseline;
        var rgb = ParseColor(hex);
        return Color.FromArgb(baseline.A, rgb.R, rgb.G, rgb.B);
    }
}
