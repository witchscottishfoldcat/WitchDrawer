using WitchDrawer.App.Views;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowDragTests
{
    [Fact]
    public void ReleasedOutsideApp_WithExistingPath_ExportsToDesktop()
    {
        Assert.True(DesktopBoxWindow.ShouldExportItemAfterDrag(
            dragWasCanceled: false,
            canExportPath: true,
            cursorOverApp: false,
            internalDropSucceeded: false));
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    public void CanceledInvalidOrInternalDrag_DoesNotExport(
        bool dragWasCanceled,
        bool canExportPath,
        bool cursorOverApp,
        bool internalDropSucceeded)
    {
        Assert.False(DesktopBoxWindow.ShouldExportItemAfterDrag(
            dragWasCanceled,
            canExportPath,
            cursorOverApp,
            internalDropSucceeded));
    }
}
