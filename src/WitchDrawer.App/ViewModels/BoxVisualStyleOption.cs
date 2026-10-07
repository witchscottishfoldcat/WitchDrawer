using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;

namespace WitchDrawer.App.ViewModels;

public sealed class BoxVisualStyleOption(
    BoxVisualStyle style, string nameKey, string descriptionKey, string glyph) : LocalizedObservableObject
{
    public BoxVisualStyle Style { get; } = style;
    public string Name => Strings.Get(nameKey);
    public string Description => Strings.Get(descriptionKey);
    public string Glyph { get; } = glyph;
}
