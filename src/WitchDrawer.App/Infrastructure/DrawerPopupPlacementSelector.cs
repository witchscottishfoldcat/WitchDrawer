using System.Windows;

namespace WitchDrawer.App.Infrastructure;

internal enum DrawerPopupPlacement
{
    Bottom,
    Top,
    Right,
    Left,
    Center
}

internal static class DrawerPopupPlacementSelector
{
    private static readonly DrawerPopupPlacement[] PreferredPlacements =
    [
        DrawerPopupPlacement.Bottom,
        DrawerPopupPlacement.Top,
        DrawerPopupPlacement.Right,
        DrawerPopupPlacement.Left
    ];

    public static DrawerPopupPlacement Select(
        Rect anchorBounds,
        Size popupSize,
        IReadOnlyCollection<Rect> occupiedBounds,
        double gap,
        double collisionPadding,
        Rect? workArea = null)
    {
        if (!IsUsable(anchorBounds) || !IsUsable(popupSize))
        {
            return DrawerPopupPlacement.Center;
        }

        var normalizedGap = NormalizeNonNegative(gap);
        var normalizedPadding = NormalizeNonNegative(collisionPadding);
        foreach (var placement in PreferredPlacements)
        {
            var candidate = GetCandidateBounds(
                placement,
                anchorBounds,
                popupSize,
                normalizedGap);
            if (workArea is { } bounds && IsUsable(bounds) && !bounds.Contains(candidate))
            {
                // The candidate would spill off the work area; WPF would then
                // flip the popup back over the anchor, causing an overlap.
                continue;
            }

            if (!occupiedBounds.Any(occupied => Intersects(candidate, occupied, normalizedPadding)))
            {
                return placement;
            }
        }

        return DrawerPopupPlacement.Center;
    }

    internal static Rect GetCandidateBounds(
        DrawerPopupPlacement placement,
        Rect anchorBounds,
        Size popupSize,
        double gap)
    {
        var centeredLeft = anchorBounds.Left + ((anchorBounds.Width - popupSize.Width) / 2);
        var centeredTop = anchorBounds.Top + ((anchorBounds.Height - popupSize.Height) / 2);
        return placement switch
        {
            DrawerPopupPlacement.Bottom => new Rect(
                centeredLeft,
                anchorBounds.Bottom + gap,
                popupSize.Width,
                popupSize.Height),
            DrawerPopupPlacement.Top => new Rect(
                centeredLeft,
                anchorBounds.Top - popupSize.Height - gap,
                popupSize.Width,
                popupSize.Height),
            DrawerPopupPlacement.Right => new Rect(
                anchorBounds.Right + gap,
                centeredTop,
                popupSize.Width,
                popupSize.Height),
            DrawerPopupPlacement.Left => new Rect(
                anchorBounds.Left - popupSize.Width - gap,
                centeredTop,
                popupSize.Width,
                popupSize.Height),
            _ => new Rect(
                centeredLeft,
                centeredTop,
                popupSize.Width,
                popupSize.Height)
        };
    }

    private static bool Intersects(Rect candidate, Rect occupied, double padding)
    {
        if (!IsUsable(occupied))
        {
            return false;
        }

        occupied.Inflate(padding, padding);
        return candidate.IntersectsWith(occupied);
    }

    private static bool IsUsable(Rect bounds) =>
        !bounds.IsEmpty
        && double.IsFinite(bounds.X)
        && double.IsFinite(bounds.Y)
        && double.IsFinite(bounds.Width)
        && double.IsFinite(bounds.Height)
        && bounds.Width > 0
        && bounds.Height > 0;

    private static bool IsUsable(Size size) =>
        double.IsFinite(size.Width)
        && double.IsFinite(size.Height)
        && size.Width > 0
        && size.Height > 0;

    private static double NormalizeNonNegative(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
