using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopIconVisibilityTests
{
    [Fact]
    public async Task RunSetHiddenAsync_DoesNotBlockCallerWhileNativeUpdateIsPending()
    {
        using var nativeUpdateStarted = new ManualResetEventSlim();
        using var releaseNativeUpdate = new ManualResetEventSlim();

        var operation = DesktopIconVisibility.RunSetHiddenAsync(
            hidden =>
            {
                Assert.True(hidden);
                nativeUpdateStarted.Set();
                Assert.True(releaseNativeUpdate.Wait(TimeSpan.FromSeconds(5)));
            },
            hidden: true);

        Assert.True(nativeUpdateStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(operation.IsCompleted);

        releaseNativeUpdate.Set();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }

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

    [Theory]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("SHELLDLL_DefView", true)]
    [InlineData("SysListView32", false)]
    [InlineData("WitchDrawer", false)]
    [InlineData(null, false)]
    public void IsDesktopHostClass_RecognizesOnlyDesktopHosts(string? className, bool expected)
    {
        Assert.Equal(expected, DesktopIconVisibility.IsDesktopHostClass(className));
    }
}
