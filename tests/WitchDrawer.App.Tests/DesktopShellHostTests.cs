using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopShellHostTests
{
    [Fact]
    public void Windows11_KeepsShellOwnerWithoutSearchingForWorker()
    {
        Assert.Equal((nint)10, DesktopShellHost.ResolveOwner(
            (nint)10, false,
            _ => throw new InvalidOperationException("Must not inspect the Windows 11 desktop."),
            _ => throw new InvalidOperationException("Must not enumerate Windows 11 workers.")));
    }

    [Fact]
    public void MissingShell_DoesNotAttachToUnrelatedWorker()
    {
        Assert.Equal(nint.Zero, DesktopShellHost.ResolveOwner(
            nint.Zero, true,
            _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void Windows10_UsesProgmanWhenItContainsDesktopView()
    {
        Assert.Equal((nint)10, DesktopShellHost.ResolveOwner(
            (nint)10, true,
            window => window == (nint)10,
            _ => throw new InvalidOperationException("No worker lookup needed.")));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(0, 10)]
    public void Windows10_UsesDesktopWorkerOrFallsBackToShell(long worker, long expected)
    {
        Assert.Equal((nint)expected, DesktopShellHost.ResolveOwner(
            (nint)10, true, _ => false,
            shell =>
            {
                Assert.Equal((nint)10, shell);
                return (nint)worker;
            }));
    }

    [Fact]
    public void Windows10_ReResolvesAfterDesktopViewMigrates()
    {
        var viewInProgman = true;
        nint Resolve() => DesktopShellHost.ResolveOwner(
            (nint)10, true, _ => viewInProgman, _ => (nint)20);

        Assert.Equal((nint)10, Resolve());
        viewInProgman = false;
        Assert.Equal((nint)20, Resolve());
        viewInProgman = true;
        Assert.Equal((nint)10, Resolve());
    }
}
