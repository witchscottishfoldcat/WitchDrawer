using System.Windows;
using System.Windows.Controls;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class PluginsPageView : UserControl
{
    public PluginsPageView() => InitializeComponent();
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PluginsViewModel model) await model.LoadAsync();
    }
}
