using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class QuickPanelWindow : Window
{
    private bool _forceClose;

    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    public QuickPanelViewModel ViewModel => (QuickPanelViewModel)DataContext;

    public async Task ToggleAsync()
    {
        if (IsVisible && IsActive)
        {
            Hide();
            return;
        }

        await ViewModel.LoadAsync();
        Show();
        WindowMotion.PopIn(this, 0.97, 130);
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void OnItemsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var item = ItemsControl.ContainerFromElement((ItemsControl)sender, source) as FrameworkElement;
            if (item?.DataContext is DrawerItemViewModel drawerItem)
            {
                await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
                Hide();
            }
        }
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && QuickItems.SelectedItem is DrawerItemViewModel drawerItem)
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
            Hide();
            e.Handled = true;
        }
    }
}
