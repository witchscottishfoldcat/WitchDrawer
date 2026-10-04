using System.Security.Principal;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class ProcessElevationTests
{
    [Fact]
    public void IsCurrentProcessElevated_MatchesEffectiveAdministratorMembership()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        Assert.Equal(
            principal.IsInRole(WindowsBuiltInRole.Administrator),
            ProcessElevation.IsCurrentProcessElevated());
    }
}
