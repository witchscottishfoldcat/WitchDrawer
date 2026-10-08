using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class DesktopDisplayMessagesTests
{
    [Theory]
    [InlineData(0x007E, 32, true)]
    [InlineData(0x007E, 0, true)]
    [InlineData(0x001A, 0x002F, true)]
    [InlineData(0x001A, 0, false)]
    [InlineData(0x001A, 0x0014, false)]
    [InlineData(0x0047, 0, false)]
    public void DisplayModeAndWorkAreaChanges_RequestLayoutRecovery(int message, int parameter, bool expected)
        => Assert.Equal(expected, DesktopDisplayMessages.ChangesLayout(message, (nint)parameter));
}
