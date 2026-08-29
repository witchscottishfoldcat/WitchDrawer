using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

/// <summary>
/// The fixed m x n size mode was removed (all boxes are adaptive-only).
/// Only the BoxSizeModeState value-type serialization/clamping rules remain.
/// </summary>
public sealed class BoxSizeSettingsTests
{
    [Fact]
    public void SizeModeState_RoundTripsThroughSerialization()
    {
        var fixedState = new BoxSizeModeState(true, 3, 2);

        Assert.Equal("Fixed:3:2", fixedState.Serialize());
        Assert.Equal(fixedState, BoxSizeModeState.Parse(fixedState.Serialize()));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse("Adaptive"));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse(null));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse("garbage"));
    }

    [Fact]
    public void SizeModeState_ClampsOutOfRangeValues()
    {
        var parsed = BoxSizeModeState.Parse("Fixed:1001:0");

        Assert.Equal(new BoxSizeModeState(true, BoxSizeModeState.MaxColumns, BoxSizeModeState.MinCells), parsed);
    }

    [Fact]
    public void SizeModeState_PreservesValuesBeyondLegacyViewportLimits()
    {
        Assert.Equal(
            new BoxSizeModeState(true, 13, 9),
            BoxSizeModeState.Parse("Fixed:13:9"));
    }

    [Fact]
    public void SizeModeState_FitsExtent_EnforcesLowerBound()
    {
        Assert.True(new BoxSizeModeState(true, 3, 2).FitsExtent(3, 2));
        Assert.False(new BoxSizeModeState(true, 2, 2).FitsExtent(3, 2));
        Assert.False(new BoxSizeModeState(true, 3, 1).FitsExtent(3, 2));
        Assert.True(BoxSizeModeState.Adaptive.FitsExtent(12, 8));
    }
}