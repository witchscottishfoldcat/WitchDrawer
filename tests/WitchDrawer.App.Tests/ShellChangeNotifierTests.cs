using WitchDrawer.Native.Files;

namespace WitchDrawer.App.Tests;

public sealed class ShellChangeNotifierTests
{
    [Theory]
    [InlineData(true, 0x00000008u)]   // directory -> SHCNE_MKDIR
    [InlineData(false, 0x00000002u)]  // file -> SHCNE_CREATE
    public void GetCreateEvent_MapsDirectoryFlag(bool isDirectory, uint expectedEvent)
    {
        Assert.Equal(expectedEvent, ShellChangeNotifier.GetCreateEvent(isDirectory));
    }
}
