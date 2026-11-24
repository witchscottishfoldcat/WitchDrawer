using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WitchDrawer.App.Infrastructure;

internal static class InputTree
{
    public static DependencyObject? GetParent(DependencyObject element) => element switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(element),
        ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
        _ => null
    };
}
