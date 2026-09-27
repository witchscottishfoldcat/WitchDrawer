using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopLayerMonitorTests
{
    [Theory]
    [InlineData(0x0003u, false, "OtherApp", true)]
    [InlineData(0x8004u, true, "", true)]
    [InlineData(0x8004u, false, "WorkerW", true)]
    [InlineData(0x8004u, false, "Progman", true)]
    [InlineData(0x8000u, false, "SHELLDLL_DefView", true)]
    [InlineData(0x8002u, false, "WorkerW", true)]
    [InlineData(0x8003u, false, "WorkerW", true)]
    [InlineData(0x8004u, false, "OtherApp", false)]
    [InlineData(0x8004u, false, "SysListView32", false)]
    [InlineData(0x800Cu, false, "WorkerW", false)]
    public void RelevantEvents_IncludeLateDesktopReorderButExcludeControlChanges(
        uint type, bool desktop, string name, bool expected) =>
        Assert.Equal(expected, DesktopLayerMonitor.IsRelevantEvent(type, desktop, name));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(-4, 0, true)]
    [InlineData(-4, 1, false)]
    [InlineData(-9, 0, false)]
    public void WindowObject_DoesNotIncludeItems(int objectId, int childId, bool expected) =>
        Assert.Equal(expected, DesktopLayerMonitor.IsWindowObject(objectId, childId));

    [Fact]
    public void Dispose_IsIdempotent() => DesktopLayerUpdateQueueTests.RunSta(() =>
    {
        var monitor = new DesktopLayerMonitor();
        monitor.Dispose();
        monitor.Dispose();
    });
}
