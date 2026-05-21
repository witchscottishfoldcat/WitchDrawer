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

        if (e.Key == Key.Enter)
        {
            var item = QuickItems.SelectedItem as DrawerItemViewModel;
            if (item is null && QuickItems.Items.Count > 0)
            {
                item = QuickItems.Items[0] as DrawerItemViewModel;
            }
            if (item is not null)
            {
                await ViewModel.OpenItemCommand.ExecuteAsync(item);
                Hide();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Down)
        {
            if (QuickItems.Items.Count > 0)
            {
                int nextIndex = QuickItems.SelectedIndex + 1;
                if (nextIndex >= QuickItems.Items.Count)
                {
                    nextIndex = 0;
                }
                QuickItems.SelectedIndex = nextIndex;
                QuickItems.ScrollIntoView(QuickItems.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (QuickItems.Items.Count > 0)
            {
                int prevIndex = QuickItems.SelectedIndex - 1;
                if (prevIndex < 0)
                {
                    prevIndex = QuickItems.Items.Count - 1;
                }
                QuickItems.SelectedIndex = prevIndex;
                QuickItems.ScrollIntoView(QuickItems.SelectedItem);
            }
            e.Handled = true;
        }
    }
}
