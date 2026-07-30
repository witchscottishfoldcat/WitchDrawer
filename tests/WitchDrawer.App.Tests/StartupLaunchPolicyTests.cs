using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class StartupLaunchPolicyTests
{
    [Fact]
    public void SilentArgument_StartsHiddenAndDoesNotActivateExistingInstance()
    {
        var arguments = new[] { StartupLaunchPolicy.SilentArgument };

        Assert.True(StartupLaunchPolicy.IsSilent(arguments));
        Assert.False(StartupLaunchPolicy.ShouldActivateExistingInstance(arguments));
    }

    [Fact]
    public void SilentArgument_IsCaseInsensitive()
    {
        var arguments = new[] { "--SILENT" };

        Assert.True(StartupLaunchPolicy.IsSilent(arguments));
        Assert.False(StartupLaunchPolicy.ShouldActivateExistingInstance(arguments));
    }

    [Fact]
    public void NormalLaunch_ActivatesExistingInstance()
    {
        var arguments = Array.Empty<string>();

        Assert.False(StartupLaunchPolicy.IsSilent(arguments));
        Assert.True(StartupLaunchPolicy.ShouldActivateExistingInstance(arguments));
    }
}
