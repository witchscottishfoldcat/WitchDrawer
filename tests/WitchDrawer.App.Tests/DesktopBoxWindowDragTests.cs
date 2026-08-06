using System.Windows.Media;
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

    [Theory]
    [InlineData(10, 10)]   // top-left corner
    [InlineData(50, 50)]   // center
    [InlineData(110, 80)]  // bottom-right edge
    public void ScreenPointInsideBounds_ReturnsTrue(int x, int y)
    {
        var topLeft = new System.Windows.Point(10, 10);
        var bottomRight = new System.Windows.Point(110, 80);

        Assert.True(DesktopBoxWindow.IsScreenPointInside(x, y, topLeft, bottomRight));
    }

    [Theory]
    [InlineData(9, 50)]    // left of bounds
    [InlineData(111, 50)]  // right of bounds
    [InlineData(50, 9)]    // above bounds
    [InlineData(50, 81)]   // below bounds
    public void ScreenPointOutsideBounds_ReturnsFalse(int x, int y)
    {
        var topLeft = new System.Windows.Point(10, 10);
        var bottomRight = new System.Windows.Point(110, 80);

        Assert.False(DesktopBoxWindow.IsScreenPointInside(x, y, topLeft, bottomRight));
    }

    [Fact]
    public void PopupDragSource_AcceptsPopupRootAndItsDescendants()
    {
        var root = new ContainerVisual();
        var child = new DrawingVisual();
        root.Children.Add(child);

        Assert.True(DesktopBoxWindow.IsSameOrVisualDescendant(root, root));
        Assert.True(DesktopBoxWindow.IsSameOrVisualDescendant(root, child));
        Assert.False(DesktopBoxWindow.IsSameOrVisualDescendant(root, new DrawingVisual()));
    }
}
