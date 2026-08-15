using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxHeaderVisibilityTests
{
    [Theory]
    [InlineData(true, false, false, 0, 6, 24)]
    [InlineData(false, true, false, 0, 6, 0)]
    [InlineData(false, false, false, 0, 6, 6)]
    [InlineData(false, false, true, 1, 6, 5)]
    public void CalculateHeaderRowHeight_BalancesHiddenContentMargins(
        bool isHeaderVisible,
        bool isDrawerBox,
        bool isMappingListMode,
        double contentTopMargin,
        double contentBottomMargin,
        double expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxViewModel.CalculateHeaderRowHeight(
                isHeaderVisible,
                isDrawerBox,
                isMappingListMode,
                contentTopMargin,
                contentBottomMargin));
    }

    [Theory]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, true, true)]
    public void ShouldShowHeader_AdaptsToTitleAndExpandedDrawerState(
        bool isDrawerBox,
        bool isDrawerExpanded,
        bool isTitleVisible,
        bool isRolledUp,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxViewModel.ShouldShowHeader(
                isDrawerBox,
                isDrawerExpanded,
                isTitleVisible,
                isRolledUp));
    }
}
