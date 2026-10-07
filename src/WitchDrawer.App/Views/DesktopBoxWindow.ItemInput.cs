using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.Features.DesktopItems;
using WitchDrawer.App.Features.ItemContextMenu;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

/// <summary>
/// 图标输入：网格/封面磁贴/弹窗磁贴三套入口的按下-选中-双击-右键，以及选中清除与键盘 Delete。
/// </summary>
public partial class DesktopBoxWindow
{
    private async void OnDrawerIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerCoverTileViewModel { Item: not null } tile })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            // 双击才打开：单击只选中（与图标网格一致），避免误触直接启动。
            ClearPendingIconDrag();
            await ViewModel.OpenItemCommand.ExecuteAsync(tile.Item);
            e.Handled = true;
            return;
        }

        SelectCoverTile(tile);
        _suppressDrawerItemClick = false;
        _dragStartPoint = e.GetPosition(this);
        _dragStartItem = tile.Item;
    }

    private void SelectCoverTile(DrawerCoverTileViewModel selectedTile)
    {
        foreach (var coverTile in ViewModel.DrawerCoverTiles)
        {
            coverTile.IsSelected = ReferenceEquals(coverTile, selectedTile);
        }

        // 与网格选中互斥：任何时刻全局只有一个选中项。
        IconList.SelectedItem = null;
        FileList.SelectedItem = null;
        _keyboardDeleteTarget = selectedTile.Item;
    }

    private void OnDrawerSecondaryIconPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerItemViewModel item })
        {
            return;
        }

        _suppressDrawerItemClick = false;
        _dragStartPoint = e.GetPosition(DrawerSecondaryPopupRoot);
        _dragStartItem = item;
    }

    private async void OnDrawerSecondaryItemClick(object sender, RoutedEventArgs e)
    {
        if (_suppressDrawerItemClick)
        {
            _suppressDrawerItemClick = false;
            e.Handled = true;
            return;
        }

        if (sender is Button { DataContext: DrawerItemViewModel item })
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(item);
            e.Handled = true;
        }
    }

    private void OnApplicationDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
    }

    /// <summary>
    /// 全局鼠标钩子发现点击落在本盒子之外（桌面/其他程序/其他盒子）时调用。
    /// 盒子带 WS_EX_NOACTIVATE，外部点击不会产生任何 Deactivated 事件，
    /// 选中框只能靠这个显式信号清除。
    /// </summary>
    internal void ClearSelectionFromOutside()
    {
        _itemContextMenu.CancelPendingMenu();
        ClearItemSelection();
    }

    private void ClearItemSelection()
    {
        IconList.SelectedItem = null;
        FileList.SelectedItem = null;
        _keyboardDeleteTarget = null;
        foreach (var coverTile in ViewModel.DrawerCoverTiles)
        {
            coverTile.IsSelected = false;
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // A cancelled external OLE drag can occasionally omit the final DragLeave.
        // A subsequent real click proves that no drag is active, so remove any stale
        // target chrome before routing the click to the item or title bar.
        if (!_itemDragGate.IsEntered)
        {
            ResetAllDragVisualStates();
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsTodoBox) return;
        var selectedItem = ActiveItemsList.SelectedItem as DrawerItemViewModel ?? _keyboardDeleteTarget;
        var action = GetFileKeyboardAction(e.Key, Keyboard.Modifiers);
        if (action is not null)
        {
            e.Handled = true;
            if (action == DrawerItemContextAction.Paste || selectedItem is not null)
                await _itemContextMenu.InvokeAsync(action.Value, selectedItem);
            return;
        }
        if (e.Key is Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true;
            await ShowFileContextMenuAsync(selectedItem);
            return;
        }
        if (e.Key != Key.Delete)
        {
            return;
        }

        var itemList = ActiveItemsList;
        var item = itemList.SelectedItem as DrawerItemViewModel ?? _keyboardDeleteTarget;
        if (item is null || !ViewModel.Items.Contains(item))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.DeleteItemCommand.ExecuteAsync(item);
        _keyboardDeleteTarget = null;
        itemList.Focus();
    }

    private void OnItemsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _keyboardDeleteTarget = listBox.SelectedItem as DrawerItemViewModel;
        }
    }

    private void OnIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_itemDragGate.IsEntered)
        {
            e.Handled = true;
            return;
        }

        BeginIconDrag(e, sender as ListBox ?? ActiveItemsList);
    }

    private void SelectItem(Guid itemId)
    {
        var item = ViewModel.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return;
        }

        ActiveItemsList.SelectedItem = item;
        _keyboardDeleteTarget = item;
        ActiveItemsList.Focus();
    }

    private void ClearPendingIconDrag()
    {
        _dragStartPoint = null;
        _dragStartItem = null;
    }

    private async void OnItemsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!DesktopItemInputRules.ShouldOpenOnDoubleClick(e.ChangedButton))
        {
            return;
        }

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
        }
    }

    private void OnIconPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetDrawerItem(e.OriginalSource, out var item))
        {
            e.Handled = true;
            ClearItemSelection();
            ClearPendingIconDrag();
            _ = ShowFileContextMenuAsync(null);
            return;
        }

        e.Handled = true;
        SelectItem(item.Id);
        ClearPendingIconDrag();
        _ = ShowFileContextMenuAsync(item);
    }

    private void OnDrawerCoverIconMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerCoverTileViewModel { Item: not null } tile })
        {
            return;
        }

        e.Handled = true;
        SelectCoverTile(tile);
        ClearPendingIconDrag();
        _ = ShowFileContextMenuAsync(tile.Item);
    }

    private void OnDrawerSecondaryIconMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerItemViewModel item })
        {
            return;
        }

        e.Handled = true;
        ClearItemSelection();
        _keyboardDeleteTarget = item;
        ClearPendingIconDrag();
        _ = ShowFileContextMenuAsync(item);
    }

    internal static DrawerItemContextAction? GetFileKeyboardAction(Key key, ModifierKeys modifiers) => (key, modifiers) switch
    {
        (Key.C, ModifierKeys.Control) => DrawerItemContextAction.Copy,
        (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => DrawerItemContextAction.CopyPath,
        (Key.V, ModifierKeys.Control) => DrawerItemContextAction.Paste,
        (Key.F2, ModifierKeys.None) => DrawerItemContextAction.Rename,
        _ => null
    };

    private void OnBoxMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || ViewModel.IsTodoBox) return;
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
        e.Handled = true;
        ClearItemSelection();
        ClearPendingIconDrag();
        _ = ShowFileContextMenuAsync(null);
    }

    private int _secondaryMenuRequests;

    private async Task ShowFileContextMenuAsync(DrawerItemViewModel? item)
    {
        var popupOpen = DrawerSecondaryPopup.IsOpen;
        // Release WPF Popup's mouse capture while the detached menu is shown.
        // Otherwise the first click on the menu can be consumed closing the popup.
        if (popupOpen)
        {
            _secondaryMenuRequests++;
            DrawerSecondaryPopup.StaysOpen = true;
        }
        try
        {
            if (item is null) await _itemContextMenu.ShowBoxAsync();
            else await _itemContextMenu.ShowAsync(item);
        }
        finally
        {
            if (popupOpen && --_secondaryMenuRequests == 0) DrawerSecondaryPopup.StaysOpen = false;
        }
    }

    internal bool OwnsInputWindow(nint handle) => _itemContextMenu.OwnsWindowHandle(handle)
        || (handle != nint.Zero && DrawerSecondaryPopup.IsOpen
            && (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) as System.Windows.Interop.HwndSource)?.Handle == handle);

    private bool TryGetDrawerItem(object? source, out DrawerItemViewModel drawerItem)
    {
        drawerItem = null!;
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        var container = ItemsControl.ContainerFromElement(IconList, dependencyObject) as FrameworkElement
            ?? ItemsControl.ContainerFromElement(FileList, dependencyObject) as FrameworkElement;
        if (container?.DataContext is not DrawerItemViewModel item)
        {
            return false;
        }

        drawerItem = item;
        return true;
    }
}
