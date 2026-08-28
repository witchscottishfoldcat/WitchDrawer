using System.Globalization;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed record AutoHideSettings(
    bool IsEnabled,
    int HiddenTransparencyPercent,
    AutoHideRevealScope RevealScope,
    bool FadeWholeBox,
    bool FadeTitle,
    bool FadeBorder)
{
    public const int DefaultHiddenTransparencyPercent = 85;

    public static AutoHideSettings Defaults { get; } =
        new(false, DefaultHiddenTransparencyPercent, AutoHideRevealScope.HoveredBoxOnly, true, true, true);

    /// <summary>
    /// 隐藏时收纳盒内容（图标+文字）的可见度，0..1。与“桌面盒子透明度”
    /// （背景外观）完全独立，仅作用于内容。数值越大越不透明。
    /// </summary>
    public double ContentOpacity =>
        Math.Clamp(1 - HiddenTransparencyPercent / 100.0, 0.0, 1.0);
}

/// <summary>
/// 持久化“自动隐藏”全局设置：是否开启、隐藏时内容的透明度、悬停取消隐藏范围。
/// 与桌面盒子透明度（<see cref="AppThemeManager"/>）相互独立。
/// </summary>
public sealed class AutoHideSettingsStore(DrawerService drawerService)
{
    private const string IsEnabledSettingKey = "AutoHide.Enabled";
    private const string HiddenTransparencySettingKey = "AutoHide.HiddenTransparency";
    private const string RevealScopeSettingKey = "AutoHide.RevealScope";
    private const string FadeWholeBoxSettingKey = "AutoHide.FadeWholeBox";
    private const string FadeTitleSettingKey = "AutoHide.FadeTitle";
    private const string FadeBorderSettingKey = "AutoHide.FadeBorder";

    public async Task<AutoHideSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = AutoHideSettings.Defaults;

        var enabledRaw = await drawerService.GetSettingAsync(IsEnabledSettingKey, cancellationToken);
        if (bool.TryParse(enabledRaw, out var isEnabled) && isEnabled)
        {
            settings = settings with { IsEnabled = true };
        }

        var hiddenRaw = await drawerService.GetSettingAsync(HiddenTransparencySettingKey, cancellationToken);
        if (int.TryParse(hiddenRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hidden))
        {
            settings = settings with { HiddenTransparencyPercent = ClampHiddenPercent(hidden) };
        }

        var scopeRaw = await drawerService.GetSettingAsync(RevealScopeSettingKey, cancellationToken);
        if (Enum.TryParse<AutoHideRevealScope>(scopeRaw, ignoreCase: true, out var scope))
        {
            settings = settings with { RevealScope = scope };
        }

        var fadeBoxRaw = await drawerService.GetSettingAsync(FadeWholeBoxSettingKey, cancellationToken);
        if (bool.TryParse(fadeBoxRaw, out var fadeWholeBox))
        {
            settings = settings with { FadeWholeBox = fadeWholeBox };
        }

        var fadeTitleRaw = await drawerService.GetSettingAsync(FadeTitleSettingKey, cancellationToken);
        if (bool.TryParse(fadeTitleRaw, out var fadeTitle))
        {
            settings = settings with { FadeTitle = fadeTitle };
        }

        var fadeBorderRaw = await drawerService.GetSettingAsync(FadeBorderSettingKey, cancellationToken);
        if (bool.TryParse(fadeBorderRaw, out var fadeBorder))
        {
            settings = settings with { FadeBorder = fadeBorder };
        }

        return settings;
    }

    public async Task SaveAsync(
        AutoHideSettings settings,
        CancellationToken cancellationToken = default)
    {
        await drawerService.SetSettingAsync(
            IsEnabledSettingKey,
            settings.IsEnabled.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await drawerService.SetSettingAsync(
            HiddenTransparencySettingKey,
            ClampHiddenPercent(settings.HiddenTransparencyPercent).ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await drawerService.SetSettingAsync(
            RevealScopeSettingKey,
            settings.RevealScope.ToString(),
            cancellationToken);
        await drawerService.SetSettingAsync(
            FadeWholeBoxSettingKey,
            settings.FadeWholeBox.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await drawerService.SetSettingAsync(
            FadeTitleSettingKey,
            settings.FadeTitle.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await drawerService.SetSettingAsync(
            FadeBorderSettingKey,
            settings.FadeBorder.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
    }

    private static int ClampHiddenPercent(int hiddenTransparencyPercent)
    {
        return Math.Clamp(hiddenTransparencyPercent, 0, 100);
    }
}