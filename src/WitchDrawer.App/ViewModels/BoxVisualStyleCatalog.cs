using WitchDrawer.Core.Localization;
namespace WitchDrawer.App.ViewModels;

public static class BoxVisualStyleCatalog
{
    public static IReadOnlyList<BoxVisualStyleOption> Options { get; } =
    [
        new(
            BoxVisualStyle.Modern,
            "ModernIcons",
            "CleanRoundedIconsForEverydayUse",
            "\uE8B7"),
        new(
            BoxVisualStyle.Pixel,
            "PixelIcons",
            "RetroPixelEdgesAndDottedDetails",
            "\uE7C4")
    ];

    public static bool IsSupported(BoxVisualStyle style)
    {
        return Options.Any(option => option.Style == style);
    }

    public static BoxVisualStyleOption GetOption(BoxVisualStyle style)
    {
        return Options.First(option => option.Style == style);
    }
}
