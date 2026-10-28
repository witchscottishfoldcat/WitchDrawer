namespace WitchDrawer.App.Tests;

// These integration tests assert adjacency in the desktop's process-wide Z order.
// Other WPF tests create and show HWNDs, so they must not run alongside this band.
[CollectionDefinition("Desktop window ordering", DisableParallelization = true)]
public sealed class DesktopWindowTestCollection;
