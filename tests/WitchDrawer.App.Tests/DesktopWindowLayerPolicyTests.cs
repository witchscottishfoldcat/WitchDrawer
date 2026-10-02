using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopWindowLayerPolicyTests
{
    [Theory]
    [InlineData(10, 0, 22631, 6649, 0xA1u, true)] // Reported Pro for Workstations system.
    [InlineData(10, 0, 22631, 1, 0xA1u, true)]
    [InlineData(10, 0, 22631, 9999, 0xA1u, true)] // Other monthly updates use the same path.
    [InlineData(10, 0, 22631, 6649, 0xA2u, true)] // N edition of Pro for Workstations.
    [InlineData(10, 0, 22631, 6649, 0x30u, false)] // Professional.
    [InlineData(10, 0, 22631, 6649, 0x31u, false)] // Professional N.
    [InlineData(10, 0, 22631, 6649, 0x65u, false)] // Home.
    [InlineData(10, 0, 22631, 6649, 0x04u, false)] // Enterprise.
    [InlineData(10, 0, 22631, 6649, 0x79u, false)] // Education.
    [InlineData(10, 0, 22631, 6649, 0u, false)] // Unknown SKU.
    [InlineData(10, 0, 19045, 0, 0xA1u, false)] // Windows 10.
    [InlineData(10, 0, 22000, 0, 0xA1u, false)] // Windows 11 21H2.
    [InlineData(10, 0, 22621, 0, 0xA1u, false)] // Windows 11 22H2.
    [InlineData(10, 0, 26100, 0, 0xA1u, false)] // Windows 11 24H2.
    [InlineData(10, 0, 26200, 8246, 0x30u, false)] // This computer: Pro 25H2.
    [InlineData(10, 0, 26200, 0, 0xA1u, false)] // Workstations 25H2 is outside the reported cohort.
    [InlineData(11, 0, 22631, 0, 0xA1u, false)]
    [InlineData(10, 1, 22631, 0, 0xA1u, false)]
    public void CompatibilityPath_OnlyMatchesWorkstations23H2(
        int major, int minor, int build, int revision, uint productType, bool expected)
    {
        Assert.Equal(expected, DesktopWindowLayerPolicy.MatchesReportedSystem(
            new Version(major, minor, build, revision), productType));
    }
}
