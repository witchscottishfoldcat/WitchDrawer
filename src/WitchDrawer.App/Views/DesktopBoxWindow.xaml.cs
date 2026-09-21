using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WitchDrawer.App.Controls;
using WitchDrawer.App.Features.DesktopItems;
using WitchDrawer.App.Features.ItemContextMenu;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Native.Windows;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

public partial class DesktopBoxWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private const double DrawerPopupGap = 8;
    private const double DrawerPopupCollisionPadding = 4;
    private const int HoverExpandDelayMs = 120;
    private const int HoverRollUpDelayMs = 300;

    private static readonly HashSet<Guid> CompletedInternalDragIds = [];
    private static readonly HashSet<Guid> CompletedInternalItemIds = [];
    private bool _forceClose;
    private Point? _dragStartPoint;
    private DrawerItemViewModel? _dragStartItem;
    private readonly DragOperationGate _itemDragGate = new();
    private readonly DrawerItemContextMenuCoordinator _itemContextMenu;
    private readonly DrawerPopupAnimation _drawerPopupAnimation;
    private DrawerItemViewModel? _keyboardDeleteTarget;
    private Func<Guid, Task>? _positionChangedCallback;
    private bool _isMappingViewTransitioning;
    private Point? _mappingViewTransitionVisibleOriginPixels;
    private bool _isRollTransitioning;
    private bool _isHoverExpandedFromRollUp;
    private bool _restoreRolledUpAfterTransition;
    private bool _isSurfaceDragging;
    private CancellationTokenSource? _hoverExpandCts;
    private CancellationTokenSource? _hoverRollUpCts;
    private bool _restoreAfterMinimizeQueued;
    private bool _desktopOwnershipRestoreQueued;
    private bool _desktopIsForeground;
    private bool _isPositionLocked;
    private HwndSource? _source;
    private DesktopToolWindow? _nativeWindow;
    private double _drawerResizeStartWidth;
    private double _drawerResizeStartHeight;
    private NativePoint _drawerResizeStartCursor;
    private double _mappingListResizeStartWidth;
    private NativePoint _mappingListResizeStartCursor;
    private double _todoResizeStartWidth;
    private double _todoResizeStartHeight;
    private NativePoint _todoResizeStartCursor;
    private bool _suppressDrawerItemClick;
    private bool _isBoxOpacityRefreshQueued;
    private bool _isVisibleBoundsClampingEnabled;
    private bool _autoHideEnabled;
    private double _autoHideHiddenContentOpacity = 1;
    private bool _autoHideFadeWholeBox;
    private bool _autoHideFadeTitle;
    private bool _autoHideFadeBorder;
    private bool _autoHideRevealed;

    internal sealed class DesktopBoxDragPayload(Guid dragId, Guid itemId, Guid sourceBoxId)
    {
        private readonly TaskCompletionSource<bool> _dropCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid DragId { get; } = dragId;

        public Guid ItemId { get; } = itemId;

        public Guid SourceBoxId { get; } = sourceBoxId;

        public Guid? TargetBoxId { get; set; }

        public bool WasDroppedInsideWitchDrawer { get; set; }

        public Task<bool> DropCompletion => _dropCompletion.Task;

        public void CompleteDrop(bool succeeded)
        {
            _dropCompletion.TrySetResult(succeeded);
        }

        public static DesktopBoxDragPayload Create(Guid itemId, Guid sourceBoxId)
        {
            return new DesktopBoxDragPayload(Guid.NewGuid(), itemId, sourceBoxId);
        }
    }

    public DesktopBoxWindow(DesktopBoxViewModel viewModel)
    {
        _itemContextMenu = new DrawerItemContextMenuCoordinator(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        _drawerPopupAnimation = new DrawerPopupAnimation(DrawerSecondaryPopupRoot, DrawerSecondaryPopupScale);
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        SizeChanged += OnWindowSizeChanged;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.BoxOpacityChanged += OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged += OnDesktopBoxAppearanceChanged;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;
        PreviewMouseUp += OnWindowPreviewMouseUpForDesktopOwnership;
        // Desktop boxes often stay non-activated (ShowActivated=false + HWND_BOTTOM/NOACTIVATE).
        // Window.Deactivated therefore never runs after an external drop selection; clear when
        // the whole app loses foreground so a desktop click removes the selected-item chrome.
        Application.Current.Deactivated += OnApplicationDeactivated;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

    public void SetPositionLocked(bool isPositionLocked)
    {
        if (_isPositionLocked == isPositionLocked)
        {
            return;
        }

        _isPositionLocked = isPositionLocked;

        // A lock transition must never leave a control holding mouse capture.
        // In particular, the old drawer-cover Thumb path could keep a completed
        // locked gesture around and make the next unlocked gesture appear inert.
        if (Mouse.Captured is DependencyObject captured
            && (ReferenceEquals(captured, this) || IsAncestorOf(captured)))
        {
            Mouse.Capture(null);
        }
    }

    private ListBox ActiveItemsList => ViewModel.IsMappingListMode ? FileList : IconList;

    public void SetPositionChangedCallback(Func<Guid, Task> callback)
    {
        _positionChangedCallback = callback;
    }

    private void OnDrawerSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isPositionLocked
            || e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is not DependencyObject source
            || FindVisualAncestor<Button>(source) is not null
            || FindVisualAncestor<Thumb>(source) is not null)
        {
            return;
        }

        e.Handled = true;
        try
        {
            DragMove();
            QueueSendToBottom();
            if (_positionChangedCallback is not null)
            {
                FireAndForget.Run(
                    _positionChangedCallback(ViewModel.BoxId),
                    ViewModel.Logger,
                    $"Failed to run position callback for box {ViewModel.BoxId:N}.");
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void OnDrawerIconMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || _dragStartItem is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingIconDrag();
            return;
        }

        IInputElement coordinateSpace = ReferenceEquals(sender, DrawerSecondaryPopupRoot)
            ? DrawerSecondaryPopupRoot
            : this;
        var current = e.GetPosition(coordinateSpace);
        if (Math.Abs(current.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var drawerItem = _dragStartItem;
        ClearPendingIconDrag();
        if (!_itemDragGate.TryEnter())
        {
            return;
        }

        // 只有弹窗磁贴的拖拽要吞掉随后的 Click；封面磁贴已不挂 Click（双击才打开）。
        _suppressDrawerItemClick = ReferenceEquals(sender, DrawerSecondaryPopupRoot);
        try
        {
            await RunItemDragAsync(drawerItem, sender as UIElement ?? IconList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    public void ForceClose()
    {
        _itemContextMenu.CloseActiveMenu();
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        DrawerSecondaryPopup.IsOpen = false;
        _drawerPopupAnimation.Stop();
        CancelHoverRollUpTimers();
        RestorePersistedRollUpStateWithoutAnimation();
        if (!_forceClose)
        {
            e.Cancel = true;
            ResetDragVisualState();
            ClearPendingIconDrag();
            Hide();
            // 点击关闭必在悬停态，隐藏前 ResetDragVisualState 会把本盒留在悬停集合里；
            // 隐藏窗口不再悬停，否则从菜单恢复显示时会无悬停也一直保持 reveal。
            if (_autoHideEnabled)
            {
                AutoHideHoverLeft?.Invoke(this, EventArgs.Empty);
            }
            ViewModel.ReleaseHiddenWindowItems();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelHoverRollUpTimers();
        ViewModel.Undo.Clear();
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        DpiChanged -= OnDpiChanged;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        AppThemeManager.BoxOpacityChanged -= OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged -= OnDesktopBoxAppearanceChanged;
        Activated -= OnWindowActivated;
        Deactivated -= OnWindowDeactivated;
        StateChanged -= OnWindowStateChanged;
        PreviewMouseUp -= OnWindowPreviewMouseUpForDesktopOwnership;
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        _nativeWindow = null;
        _itemContextMenu.Dispose();
        if (Application.Current is not null)
        {
            Application.Current.Deactivated -= OnApplicationDeactivated;
        }

        base.OnClosed(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _nativeWindow = new DesktopToolWindow(handle);
        _nativeWindow.Configure();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        QueueSendToBottom();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateIconDisplayMetrics(VisualTreeHelper.GetDpi(this));
        ResetDragVisualState();
        ClearPendingIconDrag();
        ApplyThemeAppearance();
        WindowMotion.PopIn(this, 0.97, 140);
        if (ViewModel.IsTodoBox)
        {
            TodoTitleTextBox.Focus();
        }
        else
        {
            ActiveItemsList.Focus();
        }
        QueueSendToBottom();
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateIconDisplayMetrics(e.NewDpi);
    }

    private void UpdateIconDisplayMetrics(DpiScale dpi)
    {
        ViewModel.UpdateIconDisplayMetrics(dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        ApplyThemeAppearance();
    }

    private void OnBoxOpacityChanged(object? sender, ThemeBoxOpacityChangedEventArgs e)
    {
        OnDesktopBoxAppearanceChanged(sender, e.Theme);
    }

    private void OnDesktopBoxAppearanceChanged(object? sender, AppTheme theme)
    {
        if (theme != AppThemeManager.CurrentTheme || _isBoxOpacityRefreshQueued)
        {
            return;
        }

        _isBoxOpacityRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                _isBoxOpacityRefreshQueued = false;
                AppThemeManager.ApplyDesktopBoxResources(Resources);
            });
    }

    private void ApplyThemeAppearance()
    {
        AppThemeManager.ApplyDesktopBoxResources(Resources);
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        QueueSendToBottom();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
        QueueSendToBottom();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // 紧跟 DragLeave 的 DragOver 说明只是 resize churn：取消待执行的复位。
        CancelPendingDragLeaveReset();
        CancelPendingHoverRollUp();
        if (ViewModel.IsRolledUp)
        {
            RequestHoverExpand();
        }

        // OLE 拖拽期间 MouseEnter/MouseLeave 不会触发：拖拽悬停也要取消隐藏，
        // 否则拖文件到高度透明的盒上时落点不可见。重复 DragOver 由管理器侧去重。
        if (_autoHideEnabled)
        {
            AutoHideHoverEntered?.Invoke(this, EventArgs.Empty);
        }

        if (ViewModel.IsTodoBox)
        {
            ViewModel.IsDragOver = false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var acceptsDrop = false;
        var showPreview = false;
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            acceptsDrop = TryGetInternalDragPayload(e.Data, out var payload);
            // 固定模式（硬约束）：盒已满时拒绝拖入。
        if (acceptsDrop && !ViewModel.HasFreeSlotForDrop(
                payload.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null))
        {
            acceptsDrop = false;
        }

        // 排序模式的落点由排序键决定（盒内拖动为空操作），槽位预览会误导：
        // 只保留盒子高亮，不显示落点框。
        showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? DragDropEffects.Move : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, payload);
            }
        }
        else
        {
            var dropEffect = ChooseFileDropEffect(e.AllowedEffects);
            acceptsDrop = e.Data.GetDataPresent(DataFormats.FileDrop) && dropEffect != DragDropEffects.None;
            // 固定模式（硬约束）：盒已满时拒绝拖入文件。
            if (acceptsDrop && !ViewModel.HasFreeSlotForDrop())
            {
                acceptsDrop = false;
            }

            showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? dropEffect : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, null);
            }
        }

        if (!showPreview)
        {
            ViewModel.HideDragPreview();
            HideMappingListDropIndicator();
        }

        ViewModel.IsDragOver = acceptsDrop;

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        ScheduleHoverRollUp();
        // SizeToContent 窗口随拖拽预览在指针下方生长时，OLE 会补发 DragLeave/DragEnter 对
        // （churn）。若在此同步复位，就会出现"复位→下一帧 DragOver 再显示→再复位"的疯狂频闪。
        // 改为延迟复位：churn 场景紧跟的 DragOver 会取消它；真正离开/取消时没有后续
        // DragOver，复位在极短延迟后生效（肉眼不可辨）。
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _dragLeaveResetCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        FireAndForget.Run(
                ResetDragVisualStateAfterSettlingAsync(cts),
                ViewModel.Logger,
                $"Failed to reset drag visual state for box {ViewModel.BoxId:N}.");
    }

    private CancellationTokenSource? _dragLeaveResetCts;

    private async Task ResetDragVisualStateAfterSettlingAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(90, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            ResetDragVisualState();
        }
    }

    private void CancelPendingDragLeaveReset()
    {
        var cts = Interlocked.Exchange(ref _dragLeaveResetCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (ViewModel.IsTodoBox)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ResetDragVisualState();
            return;
        }

        if (!e.Data.GetDataPresent(InternalDrawerItemDragFormat)
            && !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;
        try
        {
            if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
            {
                if (TryGetInternalDragPayload(e.Data, out var payload))
                {
                    var slot = GetDropSlot(e, payload);
                    if (slot is null)
                    {
                        // 固定模式盒已满：拒绝落放，不标记为内部移动，项目保留在原盒。
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    e.Effects = DragDropEffects.Move;
                    // Mark synchronously (same object instance, in-process) so the source
                    // box sees it immediately after DoDragDrop returns and treats this as
                    // an internal move/rearrange rather than a move-out to the desktop.
                    payload.WasDroppedInsideWitchDrawer = true;
                    payload.TargetBoxId = ViewModel.BoxId;
                    FireAndForget.Run(
                        CompleteInternalDropAsync(payload, slot.Value),
                        ViewModel.Logger,
                        $"Failed to complete internal drop for box {ViewModel.BoxId:N}.");
                }

                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var slot = GetDropSlot(e);
                if (slot is null)
                {
                    // 固定模式盒已满：拒绝导入，文件保持原样。
                    e.Effects = DragDropEffects.None;
                    return;
                }

                e.Effects = paths.Length > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                ResetDragVisualState();
                ResetDragCursor();
                // ImportPathsAsync already reloads the box internally; no extra LoadAsync here.
                var importedIds = await ViewModel.ImportPathsAsync(paths, slot.Value.Column, slot.Value.Row);
                e.Effects = importedIds.Count > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                var lastImportedId = importedIds.LastOrDefault();
                var importedItem = lastImportedId != Guid.Empty
                    ? ViewModel.Items.FirstOrDefault(candidate => candidate.Id == lastImportedId)
                    : null;
                if (importedItem is not null)
                {
                    importedItem.ReloadIconIfNeeded();
                    // Only keep keyboard selection while this box actually has focus.
                    // External Explorer drops often leave the window non-activated; a sticky
                    // SelectedItem then cannot be cleared by clicking the desktop.
                    if (IsActive)
                    {
                        ActiveItemsList.SelectedItem = importedItem;
                        _keyboardDeleteTarget = importedItem;
                        ActiveItemsList.Focus();
                    }
                    else
                    {
                        ClearItemSelection();
                    }
                }
            }
        }
        finally
        {
            ResetDragVisualState();
            ResetDragCursor();
        }
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindVisualAncestor<Button>(source) is not null
                || FindVisualAncestor<TextBox>(source) is not null
                || FindVisualAncestor<Thumb>(source) is not null))
        {
            return;
        }

        if (TryGetDrawerItem(e.OriginalSource, out _))
        {
            return;
        }

        ClearItemSelection();

        if (_isPositionLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _isSurfaceDragging = true;
            try
            {
                DragMove();
                QueueSendToBottom();
                if (_positionChangedCallback is not null)
                {
                    FireAndForget.Run(
                    _positionChangedCallback(ViewModel.BoxId),
                    ViewModel.Logger,
                    $"Failed to run position callback for box {ViewModel.BoxId:N}.");
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _isSurfaceDragging = false;
                if (!IsMouseOver)
                {
                    ScheduleHoverRollUp();
                }
            }
        }
    }

    private async void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        var itemList = sender as ListBox ?? ActiveItemsList;
        if (_dragStartPoint is null || _dragStartItem is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingIconDrag();
            return;
        }

        var current = e.GetPosition(itemList);
        var distanceX = Math.Abs(current.X - _dragStartPoint.Value.X);
        var distanceY = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (distanceX < SystemParameters.MinimumHorizontalDragDistance
            && distanceY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // 按住修饰键（Ctrl/Shift）的按下-移动视为选择手势而非拖拽：
        // 否则 Ctrl+点击时的轻微抖动会误启动 OLE 拖拽，
        // 弹出虚线落点预览框，松手甚至可能误移动图标。
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            ClearPendingIconDrag();
            return;
        }

        var drawerItem = _dragStartItem;
        // DoDragDrop runs a nested OLE message loop. Clear the pending gesture and close
        // the gate before entering it so re-entrant MouseMove events cannot start a
        // second nested drag operation.
        ClearPendingIconDrag();
        if (!_itemDragGate.TryEnter())
        {
            return;
        }

        try
        {
            // 拖拽不需要窗口激活：OLE 模态循环自行处理 Esc 取消与光标反馈，
            // 激活只会把盒子抬起来闪一帧。
            await RunItemDragAsync(drawerItem, itemList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private (int Column, int Row)? GetDropSlot(DragEventArgs e, DesktopBoxDragPayload? payload = null)
    {
        var movingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
        if (ViewModel.IsMappingListMode)
        {
            return (0, GetMappingListDropIndex(e, movingItemId));
        }

        if (ViewModel.IsDrawerCollapsed)
        {
            // The collapsed drawer cover is not the item grid (the IconList is hidden and
            // has zero size), so pointer coordinates cannot select a grid cell. Append
            // after the last item, the same fallback the mapping list view uses.
            return ViewModel.GetListDropSlot(movingItemId);
        }

        var itemList = ActiveItemsList;
        var point = e.GetPosition(itemList);
        var padding = itemList.Padding;
        var rawSlot = ViewModel.GetGridSlot(
            point.X - padding.Left,
            point.Y - padding.Top,
            Math.Max(0, itemList.ActualWidth - padding.Left - padding.Right),
            Math.Max(0, itemList.ActualHeight - padding.Top - padding.Bottom));

        // 固定模式（硬约束）：盒内找不到空位时返回 null，调用方据此拒绝拖放。
        return ViewModel.TryGetAvailableDropSlot(rawSlot.Column, rawSlot.Row, movingItemId, out var slot)
            ? slot
            : null;
    }

    private int GetMappingListDropIndex(DragEventArgs e, Guid? movingItemId)
    {
        var sourceIndex = movingItemId is Guid itemId
            ? ViewModel.Items.ToList().FindIndex(item => item.Id == itemId)
            : -1;
        var source = e.OriginalSource as DependencyObject;
        var container = source is null
            ? null
            : ItemsControl.ContainerFromElement(FileList, source) as ListBoxItem;
        if (container is null)
        {
            return ViewModel.Items.Count - (sourceIndex >= 0 ? 1 : 0);
        }

        var hoveredIndex = FileList.ItemContainerGenerator.IndexFromContainer(container);
        var insertAfter = e.GetPosition(container).Y >= container.ActualHeight / 2;
        return CalculateListInsertionIndex(
            ViewModel.Items.Count,
            sourceIndex,
            hoveredIndex,
            insertAfter);
    }

    internal static int CalculateListInsertionIndex(
        int itemCount,
        int sourceIndex,
        int hoveredIndex,
        bool insertAfter)
    {
        itemCount = Math.Max(0, itemCount);
        var hasSource = sourceIndex >= 0 && sourceIndex < itemCount;
        var boundary = Math.Clamp(hoveredIndex, 0, Math.Max(0, itemCount - 1))
            + (insertAfter ? 1 : 0);
        if (hasSource && sourceIndex < boundary)
        {
            boundary--;
        }

        var remainingCount = itemCount - (hasSource ? 1 : 0);
        return Math.Clamp(boundary, 0, remainingCount);
    }

    private void ShowDropPreview(DragEventArgs e, DesktopBoxDragPayload? payload)
    {
        if (ViewModel.IsMappingListMode)
        {
            var movingItemId = payload?.SourceBoxId == ViewModel.BoxId
                ? payload.ItemId
                : (Guid?)null;
            ViewModel.HideDragPreview();
            ShowMappingListDropIndicator(e, movingItemId);
            return;
        }

        HideMappingListDropIndicator();
        if (ViewModel.IsDrawerCollapsed)
        {
            var coverMovingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
            ShowDrawerCoverDropPreview(coverMovingItemId);
            return;
        }

        var slot = GetDropSlot(e, payload);
        if (slot is null)
        {
            // 固定模式盒已满：不显示落点预览，DragOver 已给出禁止光标。
            ViewModel.HideDragPreview();
            return;
        }

        ViewModel.ShowDragPreview(slot.Value.Column, slot.Value.Row);
    }

    private void ShowMappingListDropIndicator(DragEventArgs e, Guid? movingItemId)
    {
        var insertionIndex = GetMappingListDropIndex(e, movingItemId);
        var remainingItems = ViewModel.Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .ToList();

        FrameworkElement? boundaryContainer = null;
        var useContainerBottom = false;
        if (remainingItems.Count > 0)
        {
            if (insertionIndex < remainingItems.Count)
            {
                boundaryContainer = FileList.ItemContainerGenerator.ContainerFromItem(
                    remainingItems[insertionIndex]) as FrameworkElement;
            }
            else
            {
                boundaryContainer = FileList.ItemContainerGenerator.ContainerFromItem(
                    remainingItems[^1]) as FrameworkElement;
                useContainerBottom = true;
            }
        }

        Point boundaryPoint;
        if (boundaryContainer is not null)
        {
            boundaryPoint = boundaryContainer.TranslatePoint(
                new Point(0, useContainerBottom ? boundaryContainer.ActualHeight : 0),
                MappingListDropOverlay);
        }
        else
        {
            var source = e.OriginalSource as DependencyObject;
            var hoveredContainer = source is null
                ? null
                : ItemsControl.ContainerFromElement(FileList, source) as FrameworkElement;
            boundaryPoint = hoveredContainer is null
                ? FileList.TranslatePoint(
                    new Point(0, FileList.Padding.Top),
                    MappingListDropOverlay)
                : hoveredContainer.TranslatePoint(
                    new Point(
                        0,
                        e.GetPosition(hoveredContainer).Y >= hoveredContainer.ActualHeight / 2
                            ? hoveredContainer.ActualHeight
                            : 0),
                    MappingListDropOverlay);
        }

        var listOrigin = FileList.TranslatePoint(new Point(0, 0), MappingListDropOverlay);
        var horizontalInset = Math.Max(6, FileList.Padding.Left);
        Canvas.SetLeft(MappingListInsertionIndicator, listOrigin.X + horizontalInset);
        Canvas.SetTop(MappingListInsertionIndicator, boundaryPoint.Y - 1);
        MappingListInsertionIndicator.Width = Math.Max(
            MappingListInsertionIndicator.MinWidth,
            FileList.ActualWidth - horizontalInset - Math.Max(6, FileList.Padding.Right));
        MappingListDropOverlay.Visibility = Visibility.Visible;
    }

    private void HideMappingListDropIndicator()
    {
        MappingListDropOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowDrawerCoverDropPreview(Guid? movingItemId)
    {
        // Dropped items append after the last item (see GetDropSlot), so the preview
        // frame marks the exact cover cell the item will occupy -- the same
        // "frame == landing spot" contract the normal grid boxes have.
        var insertIndex = ViewModel.Items.Count(item => movingItemId is null || item.Id != movingItemId.Value);
        if (insertIndex >= ViewModel.DrawerCoverCapacity
            || DrawerCoverItems.ActualWidth <= 0
            || DrawerCoverItems.ActualHeight <= 0)
        {
            // The item lands in the overflow popup (or the cover is not measured yet):
            // there is no cover cell to point at, keep just the box highlight.
            ViewModel.HideDragPreview();
            return;
        }

        var cellRect = CalculateCoverCellRect(
            insertIndex,
            ViewModel.DrawerCoverColumns,
            ViewModel.DrawerCoverRows,
            DrawerCoverItems.ActualWidth,
            DrawerCoverItems.ActualHeight,
            ViewModel.LayoutSettings.ItemSpacing);
        var origin = DrawerCoverItems.TranslatePoint(
            new Point(cellRect.Left, cellRect.Top),
            DragPreviewCanvas);
        ViewModel.ShowDragPreviewAt(origin.X, origin.Y, cellRect.Width, cellRect.Height);
    }

    internal static Rect CalculateCoverCellRect(
        int cellIndex,
        int columns,
        int rows,
        double surfaceWidth,
        double surfaceHeight,
        double inset)
    {
        var safeColumns = Math.Max(1, columns);
        var safeRows = Math.Max(1, rows);
        var cellWidth = surfaceWidth / safeColumns;
        var cellHeight = surfaceHeight / safeRows;
        var safeIndex = Math.Max(0, cellIndex);
        var cellColumn = safeIndex % safeColumns;
        var cellRow = safeIndex / safeColumns;
        return new Rect(
            (cellColumn * cellWidth) + inset,
            (cellRow * cellHeight) + inset,
            Math.Max(1, cellWidth - (inset * 2)),
            Math.Max(1, cellHeight - (inset * 2)));
    }

    private async Task CompleteInternalDropAsync(DesktopBoxDragPayload payload, (int Column, int Row) slot)
    {
        var moved = false;
        try
        {
            moved = await ViewModel.DropDrawerItemAsync(payload.ItemId, slot.Column, slot.Row);
            if (moved)
            {
                MarkDroppedInsideWitchDrawer(payload);
                SelectItem(payload.ItemId);
            }
        }
        finally
        {
            payload.CompleteDrop(moved);
        }
    }

    private void BeginIconDrag(MouseButtonEventArgs e, ListBox itemList)
    {
        // 不在按下时激活：盒子窗口带 WS_EX_NOACTIVATE，刻意让点选不抬升（防闪帧）。
        // 键盘激活推迟到拖拽真正开始时（OnIconMouseMove 超过阈值后）。
        _dragStartPoint = e.GetPosition(itemList);
        _dragStartItem = null;

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            itemList.SelectedItem = drawerItem;
            _keyboardDeleteTarget = drawerItem;
            _dragStartItem = drawerItem;
        }
        else
        {
            itemList.SelectedItem = null;
            _keyboardDeleteTarget = null;
        }
    }

    // A single left-button drag handles every case based on where it is released:
    //   - dropped on the same box  -> rearrange
    //   - dropped on another box   -> move into that box
    //   - dropped outside the app  -> move out to the desktop
    private async Task RunItemDragAsync(DrawerItemViewModel drawerItem, UIElement dragSource)
    {
        var payload = DesktopBoxDragPayload.Create(drawerItem.Id, ViewModel.BoxId);
        var data = new DataObject();
        data.SetData(InternalDrawerItemDragFormat, payload, autoConvert: false);
        // Existence and safety checks belong to the background Core operation.
        var canExportPath = !string.IsNullOrWhiteSpace(drawerItem.Model.StoredPath);

        var dragWasCanceled = false;
        QueryContinueDragEventHandler queryContinueDrag = (_, args) =>
        {
            if (args.EscapePressed)
            {
                dragWasCanceled = true;
            }
        };

        // The drag carries no OS file data, so the desktop/Explorer reports "no drop" and the
        // shell shows a forbidden (🚫) cursor — misleading, because releasing there still moves
        // the item to the desktop. Override the feedback: keep the normal move cursor over valid
        // in-app targets, and show a neutral hand instead of 🚫 everywhere else.
        GiveFeedbackEventHandler giveFeedback = (_, args) =>
        {
            args.Handled = true;
            if (args.Effects == DragDropEffects.None)
            {
                args.UseDefaultCursors = false;
                Mouse.SetCursor(Cursors.Hand);
            }
            else
            {
                args.UseDefaultCursors = true;
                Mouse.SetCursor(null);
            }
        };

        drawerItem.IsDragSource = true;
        dragSource.QueryContinueDrag += queryContinueDrag;
        dragSource.GiveFeedback += giveFeedback;

        // The secondary drawer popup is StaysOpen="False", so the OLE drag's mouse capture
        // would close it mid-drag and detach the drag source (killing GiveFeedback /
        // QueryContinueDrag and the cursor override). Keep it open for the drag's duration.
        var keepDrawerPopupOpen = DrawerSecondaryPopup.IsOpen
            && dragSource is Visual dragVisual
            && IsSameOrVisualDescendant(DrawerSecondaryPopupRoot, dragVisual);
        if (keepDrawerPopupOpen)
        {
            DrawerSecondaryPopup.StaysOpen = true;
        }

        try
        {
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
            var internalDropSucceeded = payload.WasDroppedInsideWitchDrawer
                || ConsumeDroppedInsideWitchDrawer(payload);
            var cursorOverWindow = IsCursorOverWitchDrawerWindow();
            var cursorOverPopup = IsCursorOverOpenDrawerPopup();
            var cursorOverApp = cursorOverWindow || cursorOverPopup;

            // OLE has finished. Release drag feedback before awaiting disk I/O.
            dragSource.GiveFeedback -= giveFeedback;
            drawerItem.IsDragSource = false;
            ResetAllDragVisualStates();
            ResetDragCursor();

            if (internalDropSucceeded)
            {
                // Dropped onto a WitchDrawer box (same box = rearrange, other box = move).
                // The destination performs the move asynchronously; wait for it to commit
                // before refreshing the source box.
                var moved = await payload.DropCompletion;
                if (moved && payload.TargetBoxId != ViewModel.BoxId)
                {
                    await ViewModel.LoadAsync();
                }
                if (!ViewModel.Items.Any(item => item.Id == drawerItem.Id))
                {
                    _keyboardDeleteTarget = null;
                }
            }
            else if (ShouldExportItemAfterDrag(
                         dragWasCanceled,
                         canExportPath,
                         cursorOverApp,
                         internalDropSucceeded))
            {
                // Released outside every WitchDrawer window → move the file to the desktop.
                var exported = await ViewModel.ExportItemToDesktopAsync(drawerItem);
                if (exported)
                {
                    _keyboardDeleteTarget = null;
                }
            }
            // else: released over the same box without moving, or cancelled with Esc → no action.
        }
        finally
        {
            if (keepDrawerPopupOpen)
            {
                DrawerSecondaryPopup.StaysOpen = false;
            }
            dragSource.QueryContinueDrag -= queryContinueDrag;
            dragSource.GiveFeedback -= giveFeedback;
            drawerItem.IsDragSource = false;
            ResetAllDragVisualStates();
            ResetDragCursor();
            if (Mouse.Captured is not null)
            {
                Mouse.Capture(null);
            }
            dragSource.Focus();
            QueueSendToBottom();
        }
    }

    internal static bool ShouldExportItemAfterDrag(
        bool dragWasCanceled,
        bool canExportPath,
        bool cursorOverApp,
        bool internalDropSucceeded)
    {
        return !dragWasCanceled
            && canExportPath
            && !cursorOverApp
            && !internalDropSucceeded;
    }

    private void ResetDragVisualState()
    {
        // 立即复位（落放/拖拽结束/全局清理）：任何延迟复位都取消。
        CancelPendingDragLeaveReset();
        ViewModel.HideDragPreview();
        HideMappingListDropIndicator();
        ViewModel.IsDragOver = false;

        // 同步自动隐藏的悬停态：指针仍在盒上（如点击触发的全局清理）保持 reveal；
        // 真正拖离或落放后指针不在盒上则恢复隐藏。拖拽中 WPF 不发鼠标事件，
        // 残留的 reveal 会在下一次鼠标移动触发 MouseEnter/Leave 时自行校正。
        if (_autoHideEnabled)
        {
            if (IsMouseOver)
            {
                AutoHideHoverEntered?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                AutoHideHoverLeft?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static void ResetAllDragVisualStates()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.ResetDragVisualState();
        }
    }

    private static void ResetDragCursor()
    {
        // GiveFeedback may leave a custom Hand cursor after DoDragDrop returns.
        Mouse.OverrideCursor = null;
        Mouse.SetCursor(null);
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
            payload = DesktopBoxDragPayload.Create(itemId, Guid.Empty);
            return true;
        }

        return false;
    }

    private static DragDropEffects ChooseFileDropEffect(DragDropEffects allowedEffects)
    {
        if ((allowedEffects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return DragDropEffects.Move;
        }

        return (allowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy
            ? DragDropEffects.Copy
            : (allowedEffects & DragDropEffects.Link) == DragDropEffects.Link
                ? DragDropEffects.Link
                : DragDropEffects.None;
    }

    internal static void MarkDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
    {
        // 目标盒的 Drop 处理器在 DoDragDrop 返回前就会同步置位 WasDroppedInsideWitchDrawer，
        // 源端靠该标志位即可识别内部落放；静态集合只是"同步标记缺失"时的兜底通道。
        // 已有同步标记时再写入集合，条目永远不会被消费（源端 || 短路），残留 ItemId 会把
        // 该项目之后的"拖出到桌面"误判成内部落放，导致首次拖出静默失效。
        if (!payload.WasDroppedInsideWitchDrawer)
        {
            CompletedInternalDragIds.Add(payload.DragId);
            CompletedInternalItemIds.Add(payload.ItemId);
        }

        payload.WasDroppedInsideWitchDrawer = true;
    }

    internal static bool ConsumeDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
    {
        var matchedByDrag = CompletedInternalDragIds.Remove(payload.DragId);
        var matchedByItem = CompletedInternalItemIds.Remove(payload.ItemId);
        var matched = matchedByDrag || matchedByItem;
        if (!matched)
        {
            return false;
        }

        payload.WasDroppedInsideWitchDrawer = true;
        return true;
    }

    private bool IsCursorOverOpenDrawerPopup()
    {
        // Popups are not part of Application.Current.Windows, so the window hit-test above
        // misses releases over the secondary drawer popup. Treat those as inside the app;
        // otherwise a short drag ending on the popup would wrongly move the item to the desktop.
        if (!DrawerSecondaryPopup.IsOpen
            || !DrawerSecondaryPopupRoot.IsVisible
            || !GetCursorPos(out var cursor))
        {
            return false;
        }

        try
        {
            var topLeft = DrawerSecondaryPopupRoot.PointToScreen(new Point(0, 0));
            var bottomRight = DrawerSecondaryPopupRoot.PointToScreen(
                new Point(DrawerSecondaryPopupRoot.ActualWidth, DrawerSecondaryPopupRoot.ActualHeight));
            return IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight);
        }
        catch (InvalidOperationException)
        {
            // Popup content has no presentation source yet; skip it.
            return false;
        }
    }

    internal static bool IsScreenPointInside(int x, int y, Point topLeft, Point bottomRight)
    {
        return x >= topLeft.X
            && x <= bottomRight.X
            && y >= topLeft.Y
            && y <= bottomRight.Y;
    }

    internal static bool IsSameOrVisualDescendant(Visual root, Visual candidate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidate);
        return ReferenceEquals(root, candidate) || root.IsAncestorOf(candidate);
    }

    private static bool IsCursorOverWitchDrawerWindow()
    {
        // Mouse.GetPosition is stale right after DoDragDrop; use the real cursor screen
        // position and compare against each window's on-screen rectangle.
        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = window.PointToScreen(new Point(0, 0));
                var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
                if (IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Window has no presentation source yet; skip it.
            }
        }

        return false;
    }

}
