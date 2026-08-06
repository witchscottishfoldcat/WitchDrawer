using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class DrawerPopupPlacementSelectorTests
{
    private static readonly Rect AnchorBounds = new(100, 100, 100, 60);
    private static readonly Size PopupSize = new(80, 50);

    [Fact]
    public void Select_PrefersBottomWhenNoBoxBlocksIt()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [],
            gap: 8,
            collisionPadding: 4);

        Assert.Equal(DrawerPopupPlacement.Bottom, placement);
    }

    [Fact]
    public void Select_UsesTopWhenBottomIsBlocked()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [new Rect(105, 164, 90, 60)],
            gap: 8,
            collisionPadding: 4);

        Assert.Equal(DrawerPopupPlacement.Top, placement);
    }

    [Fact]
    public void Select_UsesRightWhenVerticalSidesAreBlocked()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [
                new Rect(105, 164, 90, 60),
                new Rect(105, 36, 90, 60)
            ],
            gap: 8,
            collisionPadding: 4);

        Assert.Equal(DrawerPopupPlacement.Right, placement);
    }

    [Fact]
    public void Select_UsesLeftWhenBottomTopAndRightAreBlocked()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [
                new Rect(105, 164, 90, 60),
                new Rect(105, 36, 90, 60),
                new Rect(204, 100, 90, 60)
            ],
            gap: 8,
            collisionPadding: 4);

        Assert.Equal(DrawerPopupPlacement.Left, placement);
    }

    [Fact]
    public void Select_FallsBackToCenterWhenEverySideIsBlocked()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [
                new Rect(105, 164, 90, 60),
                new Rect(105, 36, 90, 60),
                new Rect(204, 100, 90, 60),
                new Rect(6, 100, 90, 60)
            ],
            gap: 8,
            collisionPadding: 4);

        Assert.Equal(DrawerPopupPlacement.Center, placement);
    }

    [Fact]
    public void Select_SkipsPlacementThatWouldSpillOffWorkArea()
    {
        // Right candidate runs 208..288, but the work area ends at x=250,
        // so Right must be skipped in favor of an in-bounds placement.
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [],
            gap: 8,
            collisionPadding: 4,
            workArea: new Rect(0, 0, 250, 400));

        Assert.Equal(DrawerPopupPlacement.Bottom, placement);
    }

    [Fact]
    public void Select_PrefersBottomWhenRightIsTheOnlyInBoundsSide()
    {
        // Bottom would spill past the work-area bottom edge, Right is blocked by
        // another box, so the selector should keep looking rather than flip back.
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [new Rect(204, 100, 90, 60)],
            gap: 8,
            collisionPadding: 4,
            workArea: new Rect(0, 0, 1000, 200));

        // Bottom candidate runs 168..218 > work area bottom 200, so it is skipped;
        // Right is blocked; Top candidate runs 42..92 which fits -> Top.
        Assert.Equal(DrawerPopupPlacement.Top, placement);
    }

    [Fact]
    public void Select_IgnoresWorkAreaWhenCandidateFitsInside()
    {
        var placement = DrawerPopupPlacementSelector.Select(
            AnchorBounds,
            PopupSize,
            [],
            gap: 8,
            collisionPadding: 4,
            workArea: new Rect(0, 0, 1000, 1000));

        Assert.Equal(DrawerPopupPlacement.Bottom, placement);
    }
}
