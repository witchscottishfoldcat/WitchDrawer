using System.Globalization;
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
}
