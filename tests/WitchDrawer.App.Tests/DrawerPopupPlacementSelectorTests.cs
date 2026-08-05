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
}
