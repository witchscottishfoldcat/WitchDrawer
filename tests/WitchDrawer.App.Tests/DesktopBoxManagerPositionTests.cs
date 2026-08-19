using System.Globalization;
using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxManagerPositionTests
{
    [Fact]
    public void SerializePosition_UsesInvariantRoundTripFormat()
    {
        var serialized = DesktopBoxManager.SerializePosition(12.5, -34.75);

        Assert.Equal("12.5,-34.75", serialized);
        Assert.True(DesktopBoxManager.TryParsePosition(serialized, out var left, out var top));
        Assert.Equal(12.5, left);
        Assert.Equal(-34.75, top);
    }

    [Fact]
    public void SerializePosition_IsIndependentOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("12.5,34.75", DesktopBoxManager.SerializePosition(12.5, 34.75));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,2,3")]
    [InlineData("bad,2")]
    [InlineData("NaN,2")]
    [InlineData("1,Infinity")]
    public void TryParsePosition_RejectsInvalidValues(string? raw)
    {
        Assert.False(DesktopBoxManager.TryParsePosition(raw, out _, out _));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, 240, 180, 840, 430)]
    [InlineData(-1920, 0, 1920, 1040, 240, 180, -1080, 430)]
    [InlineData(0, 0, 200, 100, 300, 150, 0, 0)]
    public void CalculateCenteredVisibleOrigin_CentersAndClampsInsideWorkArea(
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight,
        double visibleWidth,
        double visibleHeight,
        double expectedLeft,
        double expectedTop)
    {
        var origin = DesktopBoxManager.CalculateCenteredVisibleOrigin(
            new Size(visibleWidth, visibleHeight),
            new Rect(workLeft, workTop, workWidth, workHeight));

        Assert.Equal(expectedLeft, origin.X, 3);
        Assert.Equal(expectedTop, origin.Y, 3);
    }

    [Theory]
    [InlineData(120, 80, 240, 180, 0, 0, 1920, 1040, 120, 80)]
    [InlineData(-2100, 80, 240, 180, -1920, 0, 1920, 1040, -1920, 80)]
    [InlineData(0, 0, 300, 150, 0, 0, 200, 100, 0, 0)]
    public void CalculateClampedVisibleOrigin_KeepsRestoredBoxesInsideCurrentWorkArea(
        double boxLeft,
        double boxTop,
        double boxWidth,
        double boxHeight,
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight,
        double expectedLeft,
        double expectedTop)
    {
        var origin = DesktopBoxManager.CalculateClampedVisibleOrigin(
            new Rect(boxLeft, boxTop, boxWidth, boxHeight),
            new Rect(workLeft, workTop, workWidth, workHeight));

        Assert.Equal(expectedLeft, origin.X, 3);
        Assert.Equal(expectedTop, origin.Y, 3);
    }

    [Fact]
    public void LayoutBackup_SerializesAndParsesAllBoxPositions()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        DesktopBoxManager.LayoutBackupPosition[] positions =
        [
            new(firstId, 123.5, -42.25),
            new(secondId, -1600, 880)
        ];

        var raw = DesktopBoxManager.SerializeLayoutBackup(positions);
        var parsed = DesktopBoxManager.TryParseLayoutBackup(raw, out var restored);

        Assert.True(parsed);
        Assert.Equal(positions, restored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"Version\":2,\"Positions\":[]}")]
    [InlineData("{\"Version\":1,\"Positions\":[{\"BoxId\":\"00000000-0000-0000-0000-000000000000\",\"Left\":0,\"Top\":0}]}")]
    [InlineData("{\"Version\":1,\"Positions\":[{\"BoxId\":\"11111111-1111-1111-1111-111111111111\",\"Left\":0,\"Top\":0},{\"BoxId\":\"11111111-1111-1111-1111-111111111111\",\"Left\":10,\"Top\":10}]}")]
    public void TryParseLayoutBackup_RejectsInvalidPayloads(string? raw)
    {
        Assert.False(DesktopBoxManager.TryParseLayoutBackup(raw, out var positions));
        Assert.Empty(positions);
    }

    [Theory]
    [InlineData(1, "LayoutBackup:1")]
    [InlineData(2, "LayoutBackup:2")]
    [InlineData(3, "LayoutBackup:3")]
    public void GetLayoutBackupSettingKey_AcceptsExactlyThreeSlots(int slot, string expected)
    {
        Assert.Equal(expected, DesktopBoxManager.GetLayoutBackupSettingKey(slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void GetLayoutBackupSettingKey_RejectsSlotsOutsideRange(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DesktopBoxManager.GetLayoutBackupSettingKey(slot));
    }
}
