using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxLayoutSettingsTests
{
    [Fact]
    public void NewSettings_DefaultToSmallAndRemainIndependent()
    {
        var firstBox = new DesktopBoxLayoutSettings();
        var secondBox = new DesktopBoxLayoutSettings();

        Assert.Equal("6x6", firstBox.CurrentPreset);
        Assert.Equal("小", firstBox.CurrentSizeLabel);
        Assert.True(firstBox.IsSmallPreset);

        firstBox.ApplyPresetCommand.Execute("3x3");

        Assert.True(firstBox.IsExtraLargePreset);
        Assert.Equal("超", firstBox.CurrentSizeLabel);
        Assert.True(secondBox.IsSmallPreset);
        Assert.Equal("小", secondBox.CurrentSizeLabel);
    }

    [Fact]
    public void MappingListDimensions_FollowIconDensityPreset()
    {
        var settings = new DesktopBoxLayoutSettings();

        settings.ApplyPresetCommand.Execute("6x6");
        var smallWidth = settings.MappingListWidth;
        var smallRowHeight = settings.MappingListRowHeight;

        settings.ApplyPresetCommand.Execute("5x5");
        var mediumWidth = settings.MappingListWidth;
        var mediumRowHeight = settings.MappingListRowHeight;

        settings.ApplyPresetCommand.Execute("4x4");
        var largeWidth = settings.MappingListWidth;
        var largeRowHeight = settings.MappingListRowHeight;

        settings.ApplyPresetCommand.Execute("3x3");
        var extraLargeWidth = settings.MappingListWidth;
        var extraLargeRowHeight = settings.MappingListRowHeight;

        Assert.Equal(220, smallWidth);
        Assert.Equal(253, mediumWidth);
        Assert.Equal(286, largeWidth);
        Assert.Equal(319, extraLargeWidth);
        Assert.Equal(24, smallRowHeight);
        Assert.Equal(27.6, mediumRowHeight);
        Assert.Equal(31.2, largeRowHeight);
        Assert.Equal(34.8, extraLargeRowHeight);
        Assert.Equal(33, mediumWidth - smallWidth);
        Assert.Equal(33, largeWidth - mediumWidth);
        Assert.Equal(33, extraLargeWidth - largeWidth);

        settings.ApplyPresetCommand.Execute("6x6");
        Assert.True(settings.IsCompactPreset);
        Assert.Equal(220, settings.MappingListWidth);
        Assert.Equal(24, settings.MappingListRowHeight);
        Assert.Equal(58, settings.MappingListMinHeight);
        Assert.Equal(12.5, settings.MappingListFontSize);
        Assert.Equal(14, settings.MappingListIconSize);
    }

    [Fact]
    public void DrawerMode_UsesTheSameFourIconSizesAsNormalBoxes()
    {
        var settings = new DesktopBoxLayoutSettings(isDrawerMode: true);

        Assert.Equal("4x4", settings.CurrentPreset);
        Assert.True(settings.IsLargePreset);
        var largeCellSize = settings.DrawerCoverCellSize;
        var largeIconSize = settings.DrawerPrimaryIconSize;

        settings.ApplyPresetCommand.Execute("3x3");
        Assert.True(settings.IsExtraLargePreset);
        Assert.True(settings.DrawerCoverCellSize > largeCellSize);
        Assert.True(settings.DrawerPrimaryIconSize > largeIconSize);

        settings.ApplyPresetCommand.Execute("5x5");
        Assert.True(settings.IsMediumPreset);
        Assert.True(settings.DrawerCoverCellSize < largeCellSize);
        Assert.True(settings.DrawerPrimaryIconSize < largeIconSize);

        settings.ApplyPresetCommand.Execute("6x6");
        Assert.True(settings.IsSmallPreset);
        Assert.True(settings.DrawerPrimaryIconSize > settings.DrawerPreviewIconSize);

        settings.ApplyPresetCommand.Execute("2x2");
        Assert.Equal("6x6", settings.CurrentPreset);
    }

    [Theory]
    [InlineData("3x3", 5)]
    [InlineData("4x4", 2.5)]
    [InlineData("5x5", 2)]
    [InlineData("6x6", 1.5)]
    public void DrawerHoverGlow_StaysTwoPixelsOutsideTheIconBase(
        string preset,
        double expectedMargin)
    {
        var settings = new DesktopBoxLayoutSettings(isDrawerMode: true);
        settings.ApplyPresetWithoutCallback(preset);

        Assert.Equal(expectedMargin, settings.DrawerHoverMargin.Left);
        Assert.Equal(
            4,
            settings.DrawerCoverCellSize
            - (settings.DrawerHoverMargin.Left * 2)
            - settings.DrawerPrimaryIconFrameSize);
    }

    [Theory]
    [InlineData(80, 80, 64, 148, 84, 2, 1)]
    [InlineData(80, 120, 64, 148, 84, 2, 1)]
    [InlineData(300, 300, 64, 276, 276, 4, 4)]
    [InlineData(420.26, 180.74, 54, 398, 182, 7, 3)]
    public void DrawerResize_SnapsWidthAndHeightToIndependentGridSteps(
        double requestedWidth,
        double requestedHeight,
        double cellSize,
        double expectedWidth,
        double expectedHeight,
        int expectedColumns,
        int expectedRows)
    {
        var actual = DesktopBoxViewModel.NormalizeDrawerCoverSize(
            requestedWidth,
            requestedHeight,
            cellSize);

        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
        Assert.Equal(expectedColumns, actual.Columns);
        Assert.Equal(expectedRows, actual.Rows);
    }

    [Theory]
    [InlineData(0, 6, 0)]
    [InlineData(5, 6, 5)]
    [InlineData(6, 6, 6)]
    [InlineData(7, 6, 5)]
    [InlineData(20, 6, 5)]
    public void DrawerCover_ReservesTheLastCellOnlyWhenItemsOverflow(
        int itemCount,
        int capacity,
        int expectedDirectItemCount)
    {
        Assert.Equal(
            expectedDirectItemCount,
            DesktopBoxViewModel.CalculateDrawerDirectItemCount(itemCount, capacity));
    }

    [Theory]
    [InlineData(130, true, 121)]
    [InlineData(130, false, 130)]
    [InlineData(20, true, 11)]
    public void DrawerTitle_CompensatesItsHeightWithoutShorteningTheBox(
        double coverHeight,
        bool isTitleVisible,
        double expectedContentHeight)
    {
        Assert.Equal(
            expectedContentHeight,
            DesktopBoxViewModel.CalculateDrawerContentHeight(
                coverHeight,
                isTitleVisible));
    }

    [Theory]
    [InlineData("180,112", true, 180, 112)]
    [InlineData("320.5,240.25", true, 320.5, 240.25)]
    [InlineData("320;240", false, 0, 0)]
    [InlineData("invalid", false, 0, 0)]
    public void DrawerCoverSizeSetting_ParsesInvariantValues(
        string value,
        bool expectedResult,
        double expectedWidth,
        double expectedHeight)
    {
        var actualResult = DesktopBoxViewModel.TryParseDrawerCoverSize(
            value,
            out var actualWidth,
            out var actualHeight);

        Assert.Equal(expectedResult, actualResult);
        Assert.Equal(expectedWidth, actualWidth);
        Assert.Equal(expectedHeight, actualHeight);
    }
}
