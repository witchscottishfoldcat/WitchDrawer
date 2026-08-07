using System.Windows.Media;
using WitchDrawer.App.ViewModels;
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

    [Theory]
    [InlineData(false, false, true)]   // normal grid box: positional frame
    [InlineData(true, false, false)]   // mapping list view: no grid to point at
    [InlineData(false, true, false)]   // collapsed drawer: cover tiles are not the item grid
    [InlineData(true, true, false)]
    public void GridDragPreview_OnlyShowsForVisibleItemGrid(
        bool isMappingListMode,
        bool isDrawerCollapsed,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxViewModel.ShouldShowGridDragPreview(isMappingListMode, isDrawerCollapsed));
    }

    [Theory]
    [InlineData(0, 0.5, 0.5)]      // first cell starts at the inset
    [InlineData(1, 37.5, 0.5)]     // second column starts one cell over
    [InlineData(3, 0.5, 30.5)]     // second row starts one cell down
    public void CoverCellRect_PlacesFrameOnTheAppendedCoverCell(
        int cellIndex,
        double expectedLeft,
        double expectedTop)
    {
        // 3 columns x 2 rows over a 111 x 60 cover surface with 0.5 inset.
        var rect = DesktopBoxWindow.CalculateCoverCellRect(cellIndex, 3, 2, 111, 60, 0.5);

        Assert.Equal(expectedLeft, rect.Left);
        Assert.Equal(expectedTop, rect.Top);
        Assert.Equal(36, rect.Width);
        Assert.Equal(29, rect.Height);
    }

    [Fact]
    public void CoverCellRect_ClampsDegenerateInput()
    {
        var rect = DesktopBoxWindow.CalculateCoverCellRect(-1, 0, 0, 0, 0, 10);

        Assert.Equal(1, rect.Width);
        Assert.Equal(1, rect.Height);
    }
}

public sealed class InternalDragCompletionTests
{
    [Fact]
    public void Mark_WithSynchronousFlag_LeavesNoStaleEntriesForLaterDragOut()
    {
        // 实际 OLE 路径：目标盒在 DoDragDrop 返回前同步置位，CompleteInternalDropAsync 随后 Mark。
        // Mark 此时不得再写静态集合，否则残留 ItemId 会把该项目之后的"拖出到桌面"误判为内部落放。
        var itemId = Guid.NewGuid();
        var sourceBoxId = Guid.NewGuid();
        var payload = DesktopBoxWindow.DesktopBoxDragPayload.Create(itemId, sourceBoxId);
        payload.WasDroppedInsideWitchDrawer = true;

        DesktopBoxWindow.MarkDroppedInsideWitchDrawer(payload);

        // 之后同一项目的新拖拽（新 DragId、同 ItemId）释放到桌面：不得命中任何残留。
        var dragOut = DesktopBoxWindow.DesktopBoxDragPayload.Create(itemId, sourceBoxId);
        Assert.False(DesktopBoxWindow.ConsumeDroppedInsideWitchDrawer(dragOut));
        Assert.False(dragOut.WasDroppedInsideWitchDrawer);
    }

    [Fact]
    public void Mark_WithoutSynchronousFlag_FallbackEntryIsConsumedExactlyOnce()
    {
        // 兜底通道：同步标记缺失时写入集合，源端消费一次后即清除。
        var itemId = Guid.NewGuid();
        var sourceBoxId = Guid.NewGuid();
        var payload = DesktopBoxWindow.DesktopBoxDragPayload.Create(itemId, sourceBoxId);

        DesktopBoxWindow.MarkDroppedInsideWitchDrawer(payload);

        var first = DesktopBoxWindow.DesktopBoxDragPayload.Create(itemId, sourceBoxId);
        Assert.True(DesktopBoxWindow.ConsumeDroppedInsideWitchDrawer(first));
        Assert.True(first.WasDroppedInsideWitchDrawer);

        var second = DesktopBoxWindow.DesktopBoxDragPayload.Create(itemId, sourceBoxId);
        Assert.False(DesktopBoxWindow.ConsumeDroppedInsideWitchDrawer(second));
    }
}
