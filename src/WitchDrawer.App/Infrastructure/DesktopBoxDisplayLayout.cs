using System.Windows;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Infrastructure;

/// <summary>Keeps the user's physical origin separate from temporary display corrections.</summary>
internal sealed class DesktopBoxDisplayLayout
{
    public Point? PreferredOriginPixels { get; private set; }

    public void RememberPosition(Point originPixels)
    {
        if (double.IsFinite(originPixels.X) && double.IsFinite(originPixels.Y))
        {
            PreferredOriginPixels = originPixels;
        }
    }

    public Point? ResolveOrigin(Size windowSizePixels, Thickness marginDip, DpiScale dpi, Rect workArea)
    {
        if (PreferredOriginPixels is not Point preferred || workArea.IsEmpty
            || windowSizePixels.Width <= 0 || windowSizePixels.Height <= 0)
        {
            return null;
        }

        var visible = DesktopBoxWindow.ComputeVisibleBoundsPixels(
            new Rect(preferred, windowSizePixels), marginDip, dpi);
        var origin = DesktopBoxManager.CalculateClampedVisibleOrigin(visible, workArea);
        return DesktopBoxWindow.ComputeWindowOriginPixels(origin.X, origin.Y, marginDip, dpi);
    }
}
