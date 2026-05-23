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
    private Func<Guid, Task>? _positionChangedCallback;

    private sealed class DesktopBoxDragPayload(Guid itemId)
    {
        public Guid ItemId { get; } = itemId;

        public bool WasDroppedInsideWitchDrawer { get; set; }
    }

    public DesktopBoxWindow(DesktopBoxViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        Deactivated += OnWindowDeactivated;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

    public void SetPositionChangedCallback(Func<Guid, Task> callback)
    {
        _positionChangedCallback = callback;
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
        Deactivated -= OnWindowDeactivated;
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

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (IconList != null)
        {
            IconList.SelectedItem = null;
            _keyboardDeleteTarget = null;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        var acceptsDrop = false;
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            acceptsDrop = true;
        }
        else
        {
            acceptsDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = acceptsDrop ? DragDropEffects.Move : DragDropEffects.None;
        }

        if (acceptsDrop)
        {
            var slot = GetDropSlot(e);
            ViewModel.ShowDragPreview(slot.Column, slot.Row);
        }
        else
        {
            ViewModel.HideDragPreview();
        }

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(IconList);
        if (point.X < 0
            || point.Y < 0
            || point.X > IconList.ActualWidth
            || point.Y > IconList.ActualHeight)
        {
            ViewModel.HideDragPreview();
        }
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
            {
                if (TryGetInternalDragPayload(e.Data, out var payload))
                {
                    payload.WasDroppedInsideWitchDrawer = true;
                    var slot = GetDropSlot(e);
                    var moved = await ViewModel.DropDrawerItemAsync(payload.ItemId, slot.Column, slot.Row);
                    e.Effects = moved ? DragDropEffects.Move : DragDropEffects.None;
                    if (moved)
                    {
                        SelectItem(payload.ItemId);
                    }
                }

                e.Handled = true;
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var slot = GetDropSlot(e);
                var importedIds = await ViewModel.ImportPathsAsync(paths, slot.Column, slot.Row);
                var lastImportedId = importedIds.LastOrDefault();
                var importedItem = lastImportedId != Guid.Empty
                    ? ViewModel.Items.FirstOrDefault(candidate => candidate.Id == lastImportedId)
                    : null;
                if (importedItem is not null)
                {
                    IconList.SelectedItem = importedItem;
                    _keyboardDeleteTarget = importedItem;
                }

                IconList.Focus();
                e.Handled = true;
            }
        }
        finally
        {
            ViewModel.HideDragPreview();
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

        if (IconList != null)
        {
            IconList.SelectedItem = null;
            _keyboardDeleteTarget = null;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                if (_positionChangedCallback is not null)
                {
                    _ = _positionChangedCallback(ViewModel.BoxId);
                }
            }
            catch (InvalidOperationException)
            {
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
        else
        {
            if (IconList != null)
            {
                IconList.SelectedItem = null;
                _keyboardDeleteTarget = null;
            }
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

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            var payload = new DesktopBoxDragPayload(drawerItem.Id);
            var data = new DataObject();
            data.SetData(InternalDrawerItemDragFormat, payload);
            if (!string.IsNullOrWhiteSpace(drawerItem.PathLabel)
                && (File.Exists(drawerItem.PathLabel) || Directory.Exists(drawerItem.PathLabel)))
            {
                data.SetData(DataFormats.FileDrop, new[] { drawerItem.PathLabel });
            }

            drawerItem.IsDragSource = true;
            try
            {
                var effect = DragDrop.DoDragDrop(IconList, data, DragDropEffects.Copy | DragDropEffects.Move);
                if (!payload.WasDroppedInsideWitchDrawer && effect != DragDropEffects.None)
                {
                    await ViewModel.CompleteDragOutAsync(drawerItem);
                    _keyboardDeleteTarget = null;
                }
            }
            finally
            {
                drawerItem.IsDragSource = false;
                ViewModel.HideDragPreview();
                IconList.Focus();
            }
        }

        _dragStartPoint = null;
    }

    private (int Column, int Row) GetDropSlot(DragEventArgs e)
    {
        var point = e.GetPosition(IconList);
        return ViewModel.GetGridSlot(point.X - 8, point.Y - 8);
    }

    private void SelectItem(Guid itemId)
    {
        var item = ViewModel.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return;
        }

        IconList.SelectedItem = item;
        _keyboardDeleteTarget = item;
        IconList.Focus();
    }

    private static bool TryGetInternalDragPayload(IDataObject data, out DesktopBoxDragPayload payload)
    {
        payload = null!;
        var rawPayload = data.GetData(InternalDrawerItemDragFormat);
        if (rawPayload is DesktopBoxDragPayload typedPayload)
        {
            payload = typedPayload;
            return true;
        }

        if (rawPayload is Guid itemId)
        {
            payload = new DesktopBoxDragPayload(itemId);
            return true;
        }

        return false;
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
