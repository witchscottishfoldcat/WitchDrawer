using System.Windows.Input;

namespace WitchDrawer.App.Features.DesktopItems;

/// <summary>
/// Stable mouse semantics shared by all desktop-item surfaces.
/// </summary>
internal static class DesktopItemInputRules
{
    public static bool ShouldOpenOnDoubleClick(MouseButton changedButton) =>
        changedButton == MouseButton.Left;
}
