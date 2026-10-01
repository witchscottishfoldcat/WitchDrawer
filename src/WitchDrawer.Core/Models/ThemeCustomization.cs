namespace WitchDrawer.Core.Models;

/// <summary>Only explicit overrides are stored. Null always inherits the built-in preset.</summary>
public sealed record ThemeCustomization
{
    public int Version { get; init; } = 1;
    public double? BoxCornerRadius { get; init; }
    public double? IconCornerScale { get; init; }
    public double? ItemHoverOpacity { get; init; }
    public string? BoxBackground { get; init; }
    public string? IconBackground { get; init; }
    public string? ItemHover { get; init; }
    public string? ItemSelected { get; init; }
    public string? Accent { get; init; }

    public static ThemeCustomization Empty { get; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => this == Empty;

    public ThemeCustomization Normalize() => Version != 1 ? Empty : this with
    {
        BoxCornerRadius = NormalizeNumber(BoxCornerRadius, 0, 32),
        IconCornerScale = NormalizeNumber(IconCornerScale, 0, 2),
        ItemHoverOpacity = NormalizeNumber(ItemHoverOpacity, 0, 1),
        BoxBackground = NormalizeColor(BoxBackground),
        IconBackground = NormalizeColor(IconBackground),
        ItemHover = NormalizeColor(ItemHover),
        ItemSelected = NormalizeColor(ItemSelected),
        Accent = NormalizeColor(Accent)
    };

    public static string? NormalizeColor(string? value)
    {
        var hex = value?.Trim().TrimStart('#');
        return hex is { Length: 6 } && hex.All(Uri.IsHexDigit)
            ? "#" + hex.ToUpperInvariant() : null;
    }

    private static double? NormalizeNumber(double? value, double minimum, double maximum) =>
        value is { } number && double.IsFinite(number) ? Math.Clamp(number, minimum, maximum) : null;
}
