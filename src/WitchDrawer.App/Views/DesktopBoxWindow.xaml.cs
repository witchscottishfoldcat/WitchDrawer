using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class DesktopBoxWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private bool _forceClose;
    private Point? _dragStartPoint;
    private DrawerItemViewModel? _keyboardDeleteTarget;

    public DesktopBoxWindow(DesktopBoxViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

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
        WindowMotion.PopIn(this, 0.97, 140);
        IconList.Focus();
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await ViewModel.ImportPathsAsync(paths);
            var lastItem = ViewModel.Items.LastOrDefault();
            if (lastItem is not null)
            {
                IconList.SelectedItem = lastItem;
                _keyboardDeleteTarget = lastItem;
            }

            IconList.Focus();
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        var item = IconList.SelectedItem as DrawerItemViewModel ?? _keyboardDeleteTarget;
        if (item is null || !ViewModel.Items.Contains(item))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.DeleteItemCommand.ExecuteAsync(item);
        _keyboardDeleteTarget = null;
        IconList.Focus();
    }

    private void OnItemsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _keyboardDeleteTarget = IconList.SelectedItem as DrawerItemViewModel;
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryGetDrawerItem(e.OriginalSource, out _))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The desktop host may reject DragMove during focus transitions.
            }
        }
    }

    private void OnIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        IconList.Focus();
        Keyboard.Focus(IconList);
        _dragStartPoint = e.GetPosition(IconList);

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            IconList.SelectedItem = drawerItem;
            _keyboardDeleteTarget = drawerItem;
        }
    }

    private async void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(IconList);
        var distanceX = Math.Abs(current.X - _dragStartPoint.Value.X);
        var distanceY = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (distanceX < SystemParameters.MinimumHorizontalDragDistance
            && distanceY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem)
            && !string.IsNullOrWhiteSpace(drawerItem.PathLabel)
            && (File.Exists(drawerItem.PathLabel) || Directory.Exists(drawerItem.PathLabel)))
        {
            var data = new DataObject();
            data.SetData(InternalDrawerItemDragFormat, drawerItem.Id);
            data.SetData(DataFormats.FileDrop, new[] { drawerItem.PathLabel });

            var effect = DragDrop.DoDragDrop(IconList, data, DragDropEffects.Copy);
            if ((effect & DragDropEffects.Copy) == DragDropEffects.Copy)
            {
                await ViewModel.CompleteDragOutAsync(drawerItem);
                _keyboardDeleteTarget = null;
                IconList.Focus();
            }
        }

        _dragStartPoint = null;
    }

    private async void OnItemsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
        }
    }

    private bool TryGetDrawerItem(object? source, out DrawerItemViewModel drawerItem)
    {
        drawerItem = null!;
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        var container = ItemsControl.ContainerFromElement(IconList, dependencyObject) as FrameworkElement;
        if (container?.DataContext is not DrawerItemViewModel item)
        {
            return false;
        }

        drawerItem = item;
        return true;
    }
}
