using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxManagerShowAllTests
{
    [Fact]
    public void SnapshotHiddenWindows_RemainsStableWhenSourceDictionaryChanges()
    {
        var windows = new Dictionary<int, TestWindow>
        {
            [1] = new TestWindow(IsVisible: false),
            [2] = new TestWindow(IsVisible: true)
        };

        var hiddenWindows = DesktopBoxManager.SnapshotHiddenWindows(
            windows.Values,
            static window => window.IsVisible);

        windows.Add(3, new TestWindow(IsVisible: false));

        var hiddenWindow = Assert.Single(hiddenWindows);
        Assert.False(hiddenWindow.IsVisible);
    }

    private sealed record TestWindow(bool IsVisible);
}
