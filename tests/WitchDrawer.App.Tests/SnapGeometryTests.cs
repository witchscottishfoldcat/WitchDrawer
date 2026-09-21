using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定 <see cref="SnapGeometry.Evaluate"/> 的 10 分支对齐规则：
/// 五种垂直对齐（左对左/右对右/左贴右+间隙/右贴左-间隙/中心对中心）与
/// 镜像的五种水平对齐，含辅助线区间合并与阈值边界。从 PerformSnappingAndAlignment
/// 抽取为纯函数时的行为锚点。
/// </summary>
public sealed class SnapGeometryTests
{
    private const double Threshold = 10;
    private const double Gap = 8;

    private static Rect Dragged => new(100, 100, 200, 150);

    private static SnapAlignment Evaluate(params Rect[] others) =>
        SnapGeometry.Evaluate(Dragged, others, Threshold, Gap);

    [Fact]
    public void Vertical_LeftToLeft_SnapsAndUnionsGuideRange()
    {
        var snap = Evaluate(new Rect(102, 300, 100, 100));

        Assert.Equal(102, snap.SnappedVisibleLeft);
        Assert.Null(snap.SnappedVisibleTop);
        Assert.Equal(102, snap.VerticalGuideX);
        Assert.Equal(100, snap.VerticalGuideYMin);
        Assert.Equal(400, snap.VerticalGuideYMax);
    }

    [Fact]
    public void Vertical_RightToRight_SnapsToRightEdge()
    {
        var snap = Evaluate(new Rect(205, 300, 100, 100));

        Assert.Equal(105, snap.SnappedVisibleLeft);
        Assert.Equal(305, snap.VerticalGuideX);
    }

    [Fact]
    public void Vertical_LeftAdjacentToRightPlusGap_PlacesGuideInGapMiddle()
    {
        var snap = Evaluate(new Rect(60, 300, 40, 100));

        Assert.Equal(108, snap.SnappedVisibleLeft);
        Assert.Equal(104, snap.VerticalGuideX);
    }

    [Fact]
    public void Vertical_RightAdjacentToLeftMinusGap_PlacesGuideInGapMiddle()
    {
        var snap = Evaluate(new Rect(305, 300, 200, 100));

        Assert.Equal(97, snap.SnappedVisibleLeft);
        Assert.Equal(301, snap.VerticalGuideX);
    }

    [Fact]
    public void Vertical_CenterToCenter_SnapsToSharedCenter()
    {
        var snap = Evaluate(new Rect(196, 300, 10, 100));

        Assert.Equal(101, snap.SnappedVisibleLeft);
        Assert.Equal(201, snap.VerticalGuideX);
    }

    [Fact]
    public void Horizontal_TopToTop_SnapsAndUnionsGuideRange()
    {
        var snap = Evaluate(new Rect(500, 102, 100, 100));

        Assert.Equal(102, snap.SnappedVisibleTop);
        Assert.Null(snap.SnappedVisibleLeft);
        Assert.Equal(102, snap.HorizontalGuideY);
        Assert.Equal(100, snap.HorizontalGuideXMin);
        Assert.Equal(600, snap.HorizontalGuideXMax);
    }

    [Fact]
    public void Horizontal_BottomToBottom_SnapsToBottomEdge()
    {
        var snap = Evaluate(new Rect(500, 203, 100, 52));

        Assert.Equal(105, snap.SnappedVisibleTop);
        Assert.Equal(255, snap.HorizontalGuideY);
    }

    [Fact]
    public void Horizontal_TopAdjacentToBottomPlusGap_PlacesGuideInGapMiddle()
    {
        var snap = Evaluate(new Rect(500, 60, 100, 40));

        Assert.Equal(108, snap.SnappedVisibleTop);
        Assert.Equal(104, snap.HorizontalGuideY);
    }

    [Fact]
    public void Horizontal_BottomAdjacentToTopMinusGap_PlacesGuideInGapMiddle()
    {
        var snap = Evaluate(new Rect(500, 255, 100, 100));

        Assert.Equal(97, snap.SnappedVisibleTop);
        Assert.Equal(251, snap.HorizontalGuideY);
    }

    [Fact]
    public void Horizontal_CenterToCenter_SnapsToSharedCenter()
    {
        var snap = Evaluate(new Rect(500, 171, 10, 10));

        Assert.Equal(101, snap.SnappedVisibleTop);
        Assert.Equal(176, snap.HorizontalGuideY);
    }

    [Fact]
    public void NoAlignment_LeavesSnapsAndGuidesUnset()
    {
        var snap = Evaluate(new Rect(500, 500, 100, 100));

        Assert.Null(snap.SnappedVisibleLeft);
        Assert.Null(snap.SnappedVisibleTop);
        Assert.Null(snap.VerticalGuideX);
        Assert.Null(snap.HorizontalGuideY);
    }

    [Fact]
    public void ExactlyAtThreshold_StillSnaps()
    {
        // 阈值判断是 <=：恰好 10 像素偏差也要吸附。
        var snap = Evaluate(new Rect(110, 300, 100, 100));

        Assert.Equal(110, snap.SnappedVisibleLeft);
    }

    [Fact]
    public void MultipleAlignments_LastMatchWinsAndGuideRangeUnions()
    {
        var snap = Evaluate(
            new Rect(103, 200, 100, 100),
            new Rect(97, 400, 100, 100));

        Assert.Equal(97, snap.SnappedVisibleLeft);
        Assert.Equal(97, snap.VerticalGuideX);
        Assert.Equal(100, snap.VerticalGuideYMin);
        Assert.Equal(500, snap.VerticalGuideYMax);
    }

    [Fact]
    public void EmptyOtherBounds_ChangesNothing()
    {
        var snap = Evaluate();

        Assert.Null(snap.SnappedVisibleLeft);
        Assert.Null(snap.SnappedVisibleTop);
    }
}
