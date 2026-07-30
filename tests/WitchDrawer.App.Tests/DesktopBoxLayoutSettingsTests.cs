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
}
