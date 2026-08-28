using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定自动隐藏的核心行为：未开启时全部可见；开启后按悬停范围（仅被悬停的
/// 收纳盒 / 全部收纳盒）计算每个盒的内容可见性；隐藏透明度独立映射为内容不透明度。
/// </summary>
public sealed class AutoHideRevealTests
{
    private static readonly Guid BoxA = Guid.NewGuid();
    private static readonly Guid BoxB = Guid.NewGuid();
    private static readonly Guid BoxC = Guid.NewGuid();

    private static readonly Guid[] AllBoxes = [BoxA, BoxB, BoxC];

    private static AutoHideSettings Settings(
        bool isEnabled,
        int hiddenPercent,
        AutoHideRevealScope scope,
        bool fadeWholeBox = true,
        bool fadeTitle = true,
        bool fadeBorder = true) =>
        new(isEnabled, hiddenPercent, scope, fadeWholeBox, fadeTitle, fadeBorder);

    [Fact]
    public void ComputeAutoHideReveal_Disabled_RevealsEveryBox()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: false, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: []);

        Assert.All(AllBoxes, id => Assert.True(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_Disabled_IgnoresRevealScope()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: false, 85, AutoHideRevealScope.AllBoxes),
            hoveredBoxIds: []);

        Assert.All(AllBoxes, id => Assert.True(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_HoveredOnly_RevealsOnlyHoveredBox()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: [BoxA]);

        Assert.True(result[BoxA]);
        Assert.False(result[BoxB]);
        Assert.False(result[BoxC]);
    }

    [Fact]
    public void ComputeAutoHideReveal_HoveredOnly_WithNoHover_HidesEveryBox()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: []);

        Assert.All(AllBoxes, id => Assert.False(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_HoveredOnly_HidesEveryBoxWhenNothingIsHovered()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: []);

        Assert.DoesNotContain(BoxA, result.Where(pair => pair.Value).Select(pair => pair.Key));
    }

    [Fact]
    public void ComputeAutoHideReveal_AllBoxes_HoveringOneRevealsAll()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.AllBoxes),
            hoveredBoxIds: [BoxB]);

        Assert.All(AllBoxes, id => Assert.True(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_AllBoxes_WithNoHover_HidesEveryBox()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.AllBoxes),
            hoveredBoxIds: []);

        Assert.All(AllBoxes, id => Assert.False(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_AllBoxes_MultipleHover_StillRevealsAll()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.AllBoxes),
            hoveredBoxIds: [BoxA, BoxC]);

        Assert.All(AllBoxes, id => Assert.True(result[id]));
    }

    [Fact]
    public void ComputeAutoHideReveal_HoveredOnly_RevealsMultipleHoveredBoxesIndependently()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            AllBoxes,
            Settings(isEnabled: true, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: [BoxA, BoxC]);

        Assert.True(result[BoxA]);
        Assert.False(result[BoxB]);
        Assert.True(result[BoxC]);
    }

    [Fact]
    public void ComputeAutoHideReveal_EmptyBoxSet_ReturnsEmpty()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            [],
            Settings(isEnabled: true, 85, AutoHideRevealScope.AllBoxes),
            hoveredBoxIds: [BoxA]);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeAutoHideReveal_IgnoresHoveredIdsOutsideBoxSet()
    {
        var result = DesktopBoxManager.ComputeAutoHideReveal(
            new[] { BoxA },
            Settings(isEnabled: true, 85, AutoHideRevealScope.HoveredBoxOnly),
            hoveredBoxIds: [BoxC]);

        Assert.True(result.Count == 1);
        Assert.False(result[BoxA]);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(85, 0.15)]
    [InlineData(100, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(20, 0.8)]
    public void AutoHideSettings_ContentOpacity_MapsHiddenTransparency(
        int hiddenTransparencyPercent,
        double expectedOpacity)
    {
        // 隐藏透明度越大，内容不透明度越小，两者互斥；其与桌面盒子透明度完全分离。
        var settings = new AutoHideSettings(true, hiddenTransparencyPercent, AutoHideRevealScope.AllBoxes, true, true, true);

        Assert.Equal(expectedOpacity, settings.ContentOpacity, precision: 6);
    }

    [Fact]
    public void AutoHideSettings_ContentOpacity_ClampsOutOfRangeTransparency()
    {
        var over = new AutoHideSettings(true, 200, AutoHideRevealScope.AllBoxes, true, true, true);
        var under = new AutoHideSettings(true, -30, AutoHideRevealScope.AllBoxes, true, true, true);

        Assert.Equal(0.0, over.ContentOpacity, precision: 6);
        Assert.Equal(1.0, under.ContentOpacity, precision: 6);
    }

    [Fact]
    public void AutoHideSettings_Defaults_FadeBoxTitleAndBorder()
    {
        // 勾选“收纳盒/标题/边框”参与透明默认开启，首次启用自动隐藏时整体一并隐藏。
        Assert.True(AutoHideSettings.Defaults.FadeWholeBox);
        Assert.True(AutoHideSettings.Defaults.FadeTitle);
        Assert.True(AutoHideSettings.Defaults.FadeBorder);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void AutoHideSettings_PreservesFadeFlags(bool fadeWholeBox, bool fadeTitle, bool fadeBorder)
    {
        var settings = new AutoHideSettings(
            IsEnabled: true,
            85,
            AutoHideRevealScope.HoveredBoxOnly,
            fadeWholeBox,
            fadeTitle,
            fadeBorder);

        Assert.Equal(fadeWholeBox, settings.FadeWholeBox);
        Assert.Equal(fadeTitle, settings.FadeTitle);
        Assert.Equal(fadeBorder, settings.FadeBorder);
    }
}