namespace WitchDrawer.App.ViewModels;

public static class BoxVisualStyleCatalog
{
    public static IReadOnlyList<BoxVisualStyleOption> Options { get; } =
    [
        new(
            BoxVisualStyle.Modern,
            "现代图标",
            "清晰圆润，适合日常桌面",
            "\uE8B7"),
        new(
            BoxVisualStyle.Pixel,
            "像素图标",
            "复古像素边缘与点阵细节",
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
