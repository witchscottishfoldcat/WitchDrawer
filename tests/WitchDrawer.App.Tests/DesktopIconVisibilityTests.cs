using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopIconVisibilityTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData("1", false)]
    public void IsHiddenRegistryValue_RecognizesOnlyNonZeroRegistryNumbers(
        object? value,
        bool expected)
    {
        Assert.Equal(expected, DesktopIconVisibility.IsHiddenRegistryValue(value));
    }
}
