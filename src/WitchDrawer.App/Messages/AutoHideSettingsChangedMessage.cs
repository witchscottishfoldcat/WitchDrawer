using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Messages;

public sealed record AutoHideSettingsChangedMessage(
    bool IsEnabled,
    int HiddenPercent,
    AutoHideRevealScope RevealScope,
    bool FadeWholeBox,
    bool FadeTitle,
    bool FadeBorder);