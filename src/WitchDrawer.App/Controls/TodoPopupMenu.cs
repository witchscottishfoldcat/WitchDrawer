using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WitchDrawer.App.Controls;

internal sealed class TodoPopupMenu : ContextMenu
{
    public TodoPopupMenu(FrameworkElement anchor)
    {
        Style = anchor.TryFindResource("TodoPopupMenuStyle") as Style;
        PlacementTarget = anchor;
        Placement = PlacementMode.Bottom;
    }
}
