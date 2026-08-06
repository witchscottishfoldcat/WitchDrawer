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
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Views;

public partial class DesktopBoxWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private const double DrawerPopupGap = 8;
    private const double DrawerPopupCollisionPadding = 4;

    private static readonly HashSet<Guid> CompletedInternalDragIds = [];
    private static readonly HashSet<Guid> CompletedInternalItemIds = [];
    private bool _forceClose;
    private Point? _dragStartPoint;
    private DrawerItemViewModel? _dragStartItem;
    private readonly DragOperationGate _itemDragGate = new();
    private DrawerItemViewModel? _keyboardDeleteTarget;
    private Func<Guid, Task>? _positionChangedCallback;
    private bool _isMappingViewTransitioning;
    private bool _restoreAfterMinimizeQueued;
    private bool _desktopIsForeground;
    private bool _isPositionLocked;
    private HwndSource? _source;
    private DesktopToolWindow? _nativeWindow;
    private double _drawerResizeStartWidth;
    private double _drawerResizeStartHeight;
    private NativePoint _drawerResizeStartCursor;
    private bool _suppressDrawerItemClick;
    private bool _drawerPositionChanged;

    private sealed class DesktopBoxDragPayload(Guid dragId, Guid itemId, Guid sourceBoxId)
    {
        private readonly TaskCompletionSource<bool> _dropCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid DragId { get; } = dragId;

        public Guid ItemId { get; } = itemId;

        public Guid SourceBoxId { get; } = sourceBoxId;

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
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.CrystalBoxTransparencyChanged += OnCrystalBoxTransparencyChanged;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;
        // Desktop boxes often stay non-activated (ShowActivated=false + HWND_BOTTOM/NOACTIVATE).
        // Window.Deactivated therefore never runs after an external drop selection; clear when
        // the whole app loses foreground so a desktop click removes the selected-item chrome.
        Application.Current.Deactivated += OnApplicationDeactivated;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

    private void SendToBottom()
    {
        if (_desktopIsForeground)
        {
            _nativeWindow?.BringAboveDesktop();
        }
        else
        {
            _nativeWindow?.SendToBottom();
        }
    }

    public void QueueSendToBottom()
    {
        SendToBottom();
        Dispatcher.BeginInvoke(new Action(SendToBottom), DispatcherPriority.ApplicationIdle);
    }

    public void SetPositionLocked(bool isPositionLocked)
    {
        _isPositionLocked = isPositionLocked;
    }

    public nint NativeHandle => _nativeWindow?.Handle ?? nint.Zero;

    public bool IsNativeWindowAlive => _nativeWindow?.IsAlive == true;

    public bool RefreshDesktopHost()
    {
        return _nativeWindow?.TryAttachToDesktop() == true;
    }

    public void SetDesktopForeground(bool isForeground)
    {
        if (_desktopIsForeground == isForeground)
        {
            return;
        }

        _desktopIsForeground = isForeground;
        // Foreground monitoring is already coalesced by DesktopBoxManager.
        // Apply the resulting layer once; a second idle-time SetWindowPos is
        // visible as a flash during the Win+D compositor transition.
        SendToBottom();
    }

    private ListBox ActiveItemsList => ViewModel.IsMappingListMode ? FileList : IconList;

    public void SetPositionChangedCallback(Func<Guid, Task> callback)
    {
        _positionChangedCallback = callback;
    }

    private async void OnExpandDrawerClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ApplyDrawerItemSortAsync(ViewModel.DrawerItemSortMode);
        PrepareDrawerSecondaryPopupForOpen();
        if (sender is UIElement centerTarget)
        {
            ConfigureDrawerSecondaryPopupPlacement(centerTarget);
        }

        DrawerSecondaryPopup.IsOpen = true;
        ClearItemSelection();
        e.Handled = true;
    }

    private void PrepareDrawerSecondaryPopupForOpen()
    {
        DrawerSecondaryPopupRoot.BeginAnimation(OpacityProperty, null);
        DrawerSecondaryPopupRoot.Opacity = 0;
        PrepareDrawerPopupScaleForPlacement(DrawerSecondaryPopupScale);
    }

    internal static void PrepareDrawerPopupScaleForPlacement(ScaleTransform scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        // Popup creates its HWND using the child's current transformed bounds. A
        // reduced scale here shifts the first HWND up/left; when the animation
        // later reaches 1, the full-size content is left at that stale position.
        // Position with a neutral transform and apply the visual scale only after
        // the Popup has opened.
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
    }

    private void ConfigureDrawerSecondaryPopupPlacement(UIElement centerTarget)
    {
        var popupSize = new Size(
            ViewModel.DrawerSecondaryPanelWidth,
            ViewModel.DrawerSecondaryPanelHeight);
        var anchor = GetVisibleBounds();
        var occupiedBounds = Application.Current.Windows
            .OfType<DesktopBoxWindow>()
            .Where(window => window != this && window.IsVisible)
            .Select(window => window.GetVisibleBounds())
            .ToArray();
        var placement = DrawerPopupPlacementSelector.Select(
            anchor,
            popupSize,
            occupiedBounds,
            DrawerPopupGap,
            DrawerPopupCollisionPadding,
            SystemParameters.WorkArea);

        DrawerSecondaryPopup.HorizontalOffset = 0;
        DrawerSecondaryPopup.VerticalOffset = 0;
        if (placement == DrawerPopupPlacement.Center)
        {
            DrawerSecondaryPopup.PlacementTarget = centerTarget;
            DrawerSecondaryPopup.Placement = PlacementMode.Center;
            return;
        }

        // Keep the collision-aware side selected above. Relative Popup placement
        // can be flipped by WPF near a screen edge, potentially putting it back on
        // top of a neighboring box.
        var target = DrawerPopupPlacementSelector.GetCandidateBounds(
            placement,
            anchor,
            popupSize,
            DrawerPopupGap);
        DrawerSecondaryPopup.PlacementTarget = null;
        DrawerSecondaryPopup.Placement = PlacementMode.Absolute;
        DrawerSecondaryPopup.HorizontalOffset = target.Left;
        DrawerSecondaryPopup.VerticalOffset = target.Top;
    }

    internal Rect GetVisibleBounds()
    {
        var margin = WindowBorder.Margin;
        return new Rect(
            Left + margin.Left,
            Top + margin.Top,
            Math.Max(0, ActualWidth - margin.Left - margin.Right),
            Math.Max(0, ActualHeight - margin.Top - margin.Bottom));
    }

    private void OnDrawerSecondaryPopupOpened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                var initialScaleX = Math.Clamp(
                    ViewModel.LayoutSettings.DrawerPrimaryIconFrameSize
                        / Math.Max(1, DrawerSecondaryPopupRoot.ActualWidth),
                    0.08,
                    0.24);
                var initialScaleY = Math.Clamp(
                    ViewModel.LayoutSettings.DrawerPrimaryIconFrameSize
                        / Math.Max(1, DrawerSecondaryPopupRoot.ActualHeight),
                    0.08,
                    0.32);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var duration = TimeSpan.FromMilliseconds(190);
                DrawerSecondaryPopupRoot.CacheMode = new BitmapCache
                {
                    EnableClearType = true
                };
                DrawerSecondaryPopupScale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(initialScaleX, 1, duration) { EasingFunction = easing });
                DrawerSecondaryPopupScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(initialScaleY, 1, duration) { EasingFunction = easing });
                var opacityAnimation = new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(145))
                {
                    EasingFunction = easing
                };
                opacityAnimation.Completed += (_, _) =>
                    DrawerSecondaryPopupRoot.CacheMode = null;
                DrawerSecondaryPopupRoot.BeginAnimation(OpacityProperty, opacityAnimation);
            });
    }

    private void OnCollapseDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDrawerExpanded = false;
        ClearItemSelection();
        e.Handled = true;
    }

    private void OnDrawerResizeStarted(object sender, DragStartedEventArgs e)
    {
        _drawerResizeStartWidth = ViewModel.DrawerCoverWidth;
        _drawerResizeStartHeight = ViewModel.DrawerCoverHeight;
        GetCursorPos(out _drawerResizeStartCursor);
        e.Handled = true;
    }

    private void OnDrawerResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetCursorPos(out var currentCursor))
        {
            return;
        }

        var horizontalDelta = currentCursor.X - _drawerResizeStartCursor.X;
        var verticalDelta = currentCursor.Y - _drawerResizeStartCursor.Y;
        var dpi = VisualTreeHelper.GetDpi(this);
        ViewModel.ResizeDrawerCover(
            _drawerResizeStartWidth + (horizontalDelta / Math.Max(0.1, dpi.DpiScaleX)),
            _drawerResizeStartHeight + (verticalDelta / Math.Max(0.1, dpi.DpiScaleY)));
        e.Handled = true;
    }

    private async void OnDrawerResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        try
        {
            await ViewModel.SaveDrawerCoverSizeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.ResizeDrawerCover(
                _drawerResizeStartWidth,
                _drawerResizeStartHeight);
            _ = exception;
        }

        e.Handled = true;
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
                _ = _positionChangedCallback(ViewModel.BoxId);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnDrawerMoveDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isPositionLocked)
        {
            return;
        }

        Left += e.HorizontalChange;
        Top += e.VerticalChange;
        _drawerPositionChanged = true;
        e.Handled = true;
    }

    private void OnDrawerMoveCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_drawerPositionChanged)
        {
            return;
        }

        _drawerPositionChanged = false;
        QueueSendToBottom();
        if (_positionChangedCallback is not null)
        {
            _ = _positionChangedCallback(ViewModel.BoxId);
        }

        e.Handled = true;
    }

    private void OnDrawerIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerCoverTileViewModel { Item: not null } tile })
        {
            return;
        }

        _suppressDrawerItemClick = false;
        _dragStartPoint = e.GetPosition(this);
        _dragStartItem = tile.Item;
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

        _suppressDrawerItemClick = true;
        try
        {
            await RunItemDragAsync(drawerItem, sender as UIElement ?? IconList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private async void OnDrawerDirectItemClick(object sender, RoutedEventArgs e)
    {
        if (_suppressDrawerItemClick)
        {
            _suppressDrawerItemClick = false;
            e.Handled = true;
            return;
        }

        if (sender is Button { DataContext: DrawerCoverTileViewModel { Item: not null } tile })
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(tile.Item);
            e.Handled = true;
        }
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
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            ResetDragVisualState();
            ClearPendingIconDrag();
            Hide();
            ViewModel.ReleaseHiddenWindowItems();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        DpiChanged -= OnDpiChanged;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        AppThemeManager.CrystalBoxTransparencyChanged -= OnCrystalBoxTransparencyChanged;
        Activated -= OnWindowActivated;
        Deactivated -= OnWindowDeactivated;
        StateChanged -= OnWindowStateChanged;
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        _nativeWindow = null;
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

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (DesktopToolWindow.IsMinimizeSystemCommand(message, wordParameter))
        {
            // Win+D / Show Desktop normally minimizes top-level windows. A desktop
            // box is desktop furniture, so consume the minimize command.
            handled = true;
        }

        return nint.Zero;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_forceClose
            || WindowState != WindowState.Minimized
            || _restoreAfterMinimizeQueued)
        {
            return;
        }

        // Some shell versions minimize via ShowWindow instead of WM_SYSCOMMAND.
        // Restore after the shell's burst of Z-order changes has settled.
        _restoreAfterMinimizeQueued = true;
        _ = RestoreAfterShellMinimizeAsync();
    }

    private async Task RestoreAfterShellMinimizeAsync()
    {
        await Task.Delay(120).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _restoreAfterMinimizeQueued = false;
            if (!_forceClose && WindowState == WindowState.Minimized)
            {
                _nativeWindow?.RestoreWithoutActivation();
                // RestoreWithoutActivation no longer changes Z order. Apply exactly
                // one layer operation based on the stabilized desktop state.
                SendToBottom();
            }
        });
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

    private void OnCrystalBoxTransparencyChanged(object? sender, bool enabled)
    {
        ApplyThemeAppearance();
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

    private void OnApplicationDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
    }

    private void ClearItemSelection()
    {
        IconList.SelectedItem = null;
        FileList.SelectedItem = null;
        _keyboardDeleteTarget = null;
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnTodoTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !ViewModel.AddTodoCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.AddTodoCommand.ExecuteAsync(null);
        TodoTitleTextBox.Focus();
    }

    private async void OnUseMappingGridModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: false);
    }

    private async void OnUseMappingListModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: true);
    }

    private async Task SwitchMappingViewModeAsync(bool useListMode)
    {
        if (_isMappingViewTransitioning
            || !ViewModel.IsMappingBox
            || ViewModel.IsMappingListMode == useListMode)
        {
            return;
        }

        _isMappingViewTransitioning = true;
        var incomingList = useListMode ? FileList : IconList;

        try
        {
            var startWidth = Math.Max(MinWidth, ActualWidth);
            var startHeight = Math.Max(MinHeight, ActualHeight);

            // SizeToContent would otherwise apply the target view's desired size in one frame.
            // Freeze the current size first, then animate to the newly measured target size.
            SizeToContent = SizeToContent.Manual;
            Width = startWidth;
            Height = startHeight;
            incomingList.BeginAnimation(OpacityProperty, null);
            incomingList.Opacity = 0;

            var modeChangeTask = useListMode
                ? ViewModel.UseMappingListModeCommand.ExecuteAsync(null)
                : ViewModel.UseMappingGridModeCommand.ExecuteAsync(null);

            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.DataBind);

            WindowBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var targetWidth = Math.Max(MinWidth, WindowBorder.DesiredSize.Width);
            var targetHeight = Math.Max(MinHeight, WindowBorder.DesiredSize.Height);

            incomingList.Opacity = 1;
            incomingList.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    BeginTime = TimeSpan.FromMilliseconds(45),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            await Task.WhenAll(
                modeChangeTask,
                AnimateWindowSizeAsync(startWidth, startHeight, targetWidth, targetHeight));
        }
        finally
        {
            incomingList.BeginAnimation(OpacityProperty, null);
            incomingList.Opacity = 1;
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            _isMappingViewTransitioning = false;
            QueueSendToBottom();
        }
    }

    private Task AnimateWindowSizeAsync(
        double startWidth,
        double startHeight,
        double targetWidth,
        double targetHeight)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        Width = targetWidth;
        Height = targetHeight;

        var widthAnimation = new DoubleAnimation(startWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };
        var heightAnimation = new DoubleAnimation(startHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };
        heightAnimation.Completed += (_, _) => completion.TrySetResult();

        BeginAnimation(WidthProperty, widthAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);

        return completion.Task;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
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
            showPreview = acceptsDrop;
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
            showPreview = acceptsDrop;
            e.Effects = acceptsDrop ? dropEffect : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, null);
            }
        }

        if (!showPreview)
        {
            ViewModel.HideDragPreview();
        }

        ViewModel.IsDragOver = acceptsDrop;

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave is also raised when a drag is cancelled while the pointer is still
        // inside the list. Coordinate checks therefore leave IsDragOver stuck true.
        // If the pointer only crossed a child boundary, the next DragOver immediately
        // restores the preview.
        ResetDragVisualState();
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
                    e.Effects = DragDropEffects.Move;
                    // Mark synchronously (same object instance, in-process) so the source
                    // box sees it immediately after DoDragDrop returns and treats this as
                    // an internal move/rearrange rather than a move-out to the desktop.
                    payload.WasDroppedInsideWitchDrawer = true;
                    var slot = GetDropSlot(e, payload);
                    _ = CompleteInternalDropAsync(payload, slot);
                }

                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var slot = GetDropSlot(e);
                e.Effects = paths.Length > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                // ImportPathsAsync already reloads the box internally; no extra LoadAsync here.
                var importedIds = await ViewModel.ImportPathsAsync(paths, slot.Column, slot.Row);
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

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
            try
            {
                DragMove();
                QueueSendToBottom();
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
        if (_itemDragGate.IsEntered)
        {
            e.Handled = true;
            return;
        }

        BeginIconDrag(e, sender as ListBox ?? ActiveItemsList);
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
            await RunItemDragAsync(drawerItem, itemList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private (int Column, int Row) GetDropSlot(DragEventArgs e, DesktopBoxDragPayload? payload = null)
    {
        var movingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
        if (ViewModel.IsMappingListMode)
        {
            return ViewModel.GetListDropSlot(movingItemId);
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

        return ViewModel.GetAvailableDropSlot(rawSlot.Column, rawSlot.Row, movingItemId);
    }

    private void ShowDropPreview(DragEventArgs e, DesktopBoxDragPayload? payload)
    {
        if (ViewModel.IsDrawerCollapsed)
        {
            var coverMovingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
            ShowDrawerCoverDropPreview(coverMovingItemId);
            return;
        }

        var slot = GetDropSlot(e, payload);
        ViewModel.ShowDragPreview(slot.Column, slot.Row);
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
        // Bring the box to the foreground so keyboard input (e.g. Delete) reaches this window.
        Activate();
        itemList.Focus();
        Keyboard.Focus(itemList);
        QueueSendToBottom();
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

    private void ClearPendingIconDrag()
    {
        _dragStartPoint = null;
        _dragStartItem = null;
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
        var canExportPath = PathExists(drawerItem.PathLabel);

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

            if (internalDropSucceeded)
            {
                // Dropped onto a WitchDrawer box (same box = rearrange, other box = move).
                // The destination performs the move asynchronously; wait for it to commit
                // before refreshing the source box.
                await WaitForInternalDropAsync(payload);
                await ViewModel.LoadAsync();
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
        ViewModel.HideDragPreview();
        ViewModel.IsDragOver = false;
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

    private static async Task<bool> WaitForInternalDropAsync(DesktopBoxDragPayload payload)
    {
        var completedTask = await Task.WhenAny(payload.DropCompletion, Task.Delay(750));
        return completedTask == payload.DropCompletion && await payload.DropCompletion;
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

    private static void MarkDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
    {
        payload.WasDroppedInsideWitchDrawer = true;
        CompletedInternalDragIds.Add(payload.DragId);
        CompletedInternalItemIds.Add(payload.ItemId);
    }

    private static bool ConsumeDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
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

    private static bool PathExists(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && (File.Exists(path) || Directory.Exists(path));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

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
