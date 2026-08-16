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
using WitchDrawer.Core.Logging;
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
    private bool _isDetailViewExpanded;
    private bool _isDetailAnimating;
    private Point _lastExpandClickPoint;
    private Point? _expandOriginPosition;
    private Size? _expandOriginSize;
    private bool? _expandOriginMode; // 展开前的 IsMappingListMode；null 表示未记录
    private Point? _surfaceDragStartPoint;
    private bool _surfaceDragStarted;
    private bool _detailExpansionTriggeredOnMouseDown;
    private bool _isRollTransitioning;
    private bool _restoreAfterMinimizeQueued;
    private bool _desktopIsForeground;
    private bool _isPositionLocked;
    private HwndSource? _source;
    private DesktopToolWindow? _nativeWindow;
    private double _drawerResizeStartWidth;
    private double _drawerResizeStartHeight;
    private NativePoint _drawerResizeStartCursor;
    private bool _suppressDrawerItemClick;
    private readonly IAppLogger _logger;

    internal sealed class DesktopBoxDragPayload(Guid dragId, Guid itemId, Guid sourceBoxId)
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

    public DesktopBoxWindow(DesktopBoxViewModel viewModel, IAppLogger logger)
    {
        _logger = logger;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        SizeChanged += OnWindowSizeChanged;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.CrystalBoxTransparencyChanged += OnCrystalBoxTransparencyChanged;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;
        // Desktop boxes often stay non-activated (ShowActivated=false + HWND_BOTTOM/NOACTIVATE).
        // Window.Deactivated therefore never runs after an external drop selection; clear when
        // the whole app loses foreground so a desktop click removes the selected-item chrome.
        Application.Current.Deactivated += OnApplicationDeactivated;
        viewModel.DetailExpandDisabled += OnDetailExpandDisabled;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

    private void SendToBottom()
    {
        // 盒子永远停留在桌面层（桌面壳窗口之上、普通应用窗口之下），不再随前台状态上浮。
        // 桌面父子关系（TryAttachToDesktop）保证 Win+D 显示桌面时盒子跟随桌面一起出现。
        _nativeWindow?.SendToBottom();
    }

    public void QueueSendToBottom()
    {
        SendToBottom();
        Dispatcher.BeginInvoke(new Action(SendToBottom), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// 把所有桌面盒压回桌面层。弹窗打开时属主链被 Windows 整体提前，单个盒子沉底不够，
    /// 必须遍历所有盒子窗口统一复位。
    /// </summary>
    internal static void QueueSendToBottomAll()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.QueueSendToBottom();
        }
    }

    /// <summary>
    /// 断开弹窗 HWND 的属主关系：之后对弹窗的置顶/沉底不再沿属主链
    /// （盒子→桌面壳→所有盒子）传播。在 Opened 时同步执行，消除窗口期。
    /// </summary>
    private void DetachDrawerPopupOwner()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowLongPtr(popupSource.Handle, WindowOwnerIndex, 0);
        }
    }

    /// <summary>
    /// 弹窗按"菜单"语义激活：置顶并获取前台。弹窗已断开属主（Opened 时），激活只影响
    /// 弹窗自身——盒子不动。激活后 WPF 原生的 StaysOpen=False 完整生效：
    /// 点击桌面/其他程序/其他盒子都会自动收起，无需额外兜底。
    /// </summary>
    private void BringDrawerPopupToFront()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowPos(
                popupSource.Handle,
                WindowPositionTopmost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
            SetForegroundWindow(popupSource.Handle);
        }
    }

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

    public nint NativeHandle => _nativeWindow?.Handle ?? nint.Zero;

    public bool IsNativeWindowAlive => _nativeWindow?.IsAlive == true;

    public bool RefreshDesktopHost()
    {
        return _nativeWindow?.TryAttachToDesktop() == true;
    }

    public void SetDesktopForeground(bool isForeground)
    {
        // 层级已与前台状态解耦：盒子永远留在桌面层，这里只记录状态并保持沉底。
        _desktopIsForeground = isForeground;
        SendToBottom();
    }

    private ListBox ActiveItemsList => ViewModel.IsDetailViewMode ? DetailList : ViewModel.IsMappingListMode ? FileList : IconList;

    public void SetPositionChangedCallback(Func<Guid, Task> callback)
    {
        _positionChangedCallback = callback;
    }

    private void OnExpandDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncDrawerSecondaryFromItems();
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

    /// <summary>
    /// 初始布局稳定后强制重测。SizeToContent 窗口的首次测量以初始 HWND 尺寸为约束，
    /// 若内容之后不再变化（如折叠抽屉盒的封面），窗口会一直停留在错误的初始宽度上
    /// （封面两侧突出）。一次 InvalidateMeasure 即可让窗口贴合真实内容。
    /// </summary>
    internal void ResyncSizeToContent()
    {
        if (SizeToContent != SizeToContent.Manual)
        {
            InvalidateMeasure();
        }
    }

    internal Rect GetVisibleBounds() =>
        ComputeVisibleBounds(Left, Top, ActualWidth, ActualHeight, WindowBorder.Margin);

    /// <summary>
    /// <see cref="GetVisibleBounds"/> 的逆运算：把可视区域原点换算回窗口 Left/Top。
    /// 重叠消解在可视区域坐标系里计算，写回窗口位置时必须减去阴影留白 Margin，
    /// 否则每执行一次消解窗口就会按 Margin 平移一次（位置漂移）。
    /// </summary>
    internal void MoveToVisibleOrigin(double visibleLeft, double visibleTop)
    {
        var (left, top) = ComputeWindowOrigin(visibleLeft, visibleTop, WindowBorder.Margin);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// SizeToContent 窗口以左上角为锚点随内容向右下生长。内容尺寸变化（切换图标预设、
    /// 固定格数、增删项目）可能把右/下边缘推出工作区——表现为盒子边缘被屏幕"吞掉"。
    /// 尺寸变化后把可视区域钳回工作区；只做显示性校正，不写回已保存位置。
    /// </summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || e.PreviousSize == e.NewSize)
        {
            return;
        }

        var bounds = GetVisibleBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var workArea = GetWorkAreaDip();
        var visibleLeft = bounds.Left;
        var visibleTop = bounds.Top;
        if (bounds.Right > workArea.Right)
        {
            visibleLeft = workArea.Right - bounds.Width;
        }

        if (bounds.Bottom > workArea.Bottom)
        {
            visibleTop = workArea.Bottom - bounds.Height;
        }

        // 盒子比工作区还大时，左/上钳制优先，保证标题栏可见。
        visibleLeft = Math.Max(workArea.Left, visibleLeft);
        visibleTop = Math.Max(workArea.Top, visibleTop);
        if (Math.Abs(visibleLeft - bounds.Left) > 0.5
            || Math.Abs(visibleTop - bounds.Top) > 0.5)
        {
            MoveToVisibleOrigin(visibleLeft, visibleTop);
        }
    }

    internal static Rect ComputeVisibleBounds(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        Thickness margin) =>
        new(
            windowLeft + margin.Left,
            windowTop + margin.Top,
            Math.Max(0, windowWidth - margin.Left - margin.Right),
            Math.Max(0, windowHeight - margin.Top - margin.Bottom));

    internal static (double Left, double Top) ComputeWindowOrigin(
        double visibleLeft,
        double visibleTop,
        Thickness margin) =>
        (visibleLeft - margin.Left, visibleTop - margin.Top);

    private void OnDrawerSecondaryPopupOpened(object? sender, EventArgs e)
    {
        // 弹窗 HWND 属主是盒子窗口，盒子窗口属主是桌面壳。弹窗打开时 Windows 会把
        // 整条属主链提前：所有同属桌面壳的盒子都会被带到应用窗口之上（"全部上浮"）。
        // 第一时间断开弹窗属主，再把所有盒子压回桌面层，弹窗稍后置顶（不被沉底拖下）。
        DetachDrawerPopupOwner();
        QueueSendToBottomAll();
        // 沉底在 ApplicationIdle 还会补一次，而压主窗口沉底会把它的属子弹窗一起拖下去；
        // 置顶必须排在所有沉底调用之后，所以用 SystemIdle 优先级。
        Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, BringDrawerPopupToFront);

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
        if (e.Canceled)
        {
            // 拖拽被取消（如捕获丢失/Alt+Tab 切走）：回滚到拖拽前的尺寸，不保存。
            ViewModel.ResizeDrawerCover(_drawerResizeStartWidth, _drawerResizeStartHeight);
            e.Handled = true;
            return;
        }

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
        _keyboardDeleteTarget = null;
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
        ViewModel.DetailExpandDisabled -= OnDetailExpandDisabled;
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
        // 失活时释放空白区域的鼠标捕获（若仍持有），避免鼠标事件被持续路由到本窗口。
        // 注意：不复位 _surfaceDragStarted——若用户按下后失活再松开，应仍按"拖动结束"处理，
        // 不能让它变成一次意外的展开。
        ReleaseMouseCapture();
        ClearItemSelection();
        ResetDragVisualState();
        if (_isDetailViewExpanded)
        {
            _ = CollapseDetailViewAsync();
        }
        QueueSendToBottom();
    }

    private void OnApplicationDeactivated(object? sender, EventArgs e)
    {
        ReleaseMouseCapture();
        ClearItemSelection();
        ResetDragVisualState();
        if (_isDetailViewExpanded)
        {
            _ = CollapseDetailViewAsync();
        }
    }

    /// <summary>
    /// 全局鼠标钩子发现点击落在本盒子之外（桌面/其他程序/其他盒子）时调用。
    /// 盒子带 WS_EX_NOACTIVATE，外部点击不会产生任何 Deactivated 事件，
    /// 选中框只能靠这个显式信号清除。
    /// </summary>
    internal void ClearSelectionFromOutside()
    {
        ClearItemSelection();
        if (_isDetailViewExpanded)
        {
            _ = CollapseDetailViewAsync();
        }
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnToggleRollUpClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isRollTransitioning || _isMappingViewTransitioning || !ViewModel.SupportsRollUp)
        {
            return;
        }

        _isRollTransitioning = true;
        var rollUp = !ViewModel.IsRolledUp;
        try
        {
            var startWidth = ActualWidth;
            var startHeight = ActualHeight;
            SizeToContent = SizeToContent.Manual;
            MinHeight = 0;
            Width = startWidth;
            Height = startHeight;

            ViewModel.ApplyRollUpState(rollUp);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            WindowBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var targetHeight = WindowBorder.DesiredSize.Height;

            if (rollUp)
            {
                ViewModel.ApplyRollUpState(false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            }

            await AnimateWindowSizeAsync(startWidth, startHeight, startWidth, targetHeight);
            ViewModel.ApplyRollUpState(rollUp);
            await ViewModel.SaveRollUpStateAsync();
        }
        finally
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(MinHeightProperty);
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            _isRollTransitioning = false;
            QueueSendToBottom();
        }
    }

    private void OnTodoTitlePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 待办输入需要键盘焦点：盒子是 NOACTIVATE，点击本身不激活窗口，
        // 这里显式激活（用户明确要开始输入，盒子短暂到前面、点别处即收回）。
        Activate();
        TodoTitleTextBox.Focus();
        Keyboard.Focus(TodoTitleTextBox);
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
        if (_isDetailAnimating) // 展开/收缩动画期间忽略，避免双组 BeginAnimation 互抢
        {
            return;
        }

        ClearDetailViewState();
        await SwitchMappingViewModeAsync(useListMode: false);
    }

    private async void OnUseMappingListModeClick(object sender, RoutedEventArgs e)
    {
        if (_isDetailAnimating) // 同上
        {
            return;
        }

        ClearDetailViewState();
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

    private Task AnimateWindowPositionAsync(
        double startLeft,
        double startTop,
        double targetLeft,
        double targetTop)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var leftAnimation = new DoubleAnimation(startLeft, targetLeft, duration)
        {
            EasingFunction = easing
        };
        var topAnimation = new DoubleAnimation(startTop, targetTop, duration)
        {
            EasingFunction = easing
        };
        topAnimation.Completed += (_, _) => completion.TrySetResult();

        BeginAnimation(LeftProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnimation, HandoffBehavior.SnapshotAndReplace);

        return completion.Task;
    }

    // ---- 映射盒「详细功能」：两级展开预览 ----

    private async Task ExpandToDetailViewAsync()
    {
        if (_isDetailViewExpanded)
        {
            return;
        }

        if (_isDetailAnimating)
        {
            return;
        }

        if (ViewModel.Items.Count == 0)
        {
            // 诊断：空映射盒设计上不展开（HANDOFF 4.2）。若用户复现时盒内无条目，此日志为证。
            _logger.Info($"Box {ViewModel.BoxId}: expand skipped, box has no items.");
            return;
        }

        _isDetailAnimating = true;
        // 记录展开前的原始位置与尺寸（用于收缩时回到原位）。
        _expandOriginPosition = new Point(Left, Top);
        _expandOriginSize = new Size(ActualWidth, ActualHeight);
        _expandOriginMode = ViewModel.IsMappingListMode;

        try
        {
            // 第一级：网格 → 标准平铺（复用现有动画）
            await SwitchMappingViewModeAsync(useListMode: true);

            // 第二级：标准平铺 → 放大详细视图。动画期间若「详细功能」被关闭
            // （ClearDetailViewState 摘除动画并退出详细态），返回 false，不落放大态。
            var expanded = await AnimateToDetailViewAsync();
            if (expanded && ViewModel.IsDetailViewMode)
            {
                _isDetailViewExpanded = true;
            }
        }
        finally
        {
            _isDetailAnimating = false;
            // 动画期间视图已被外部重置（如切换按钮）：撤销详细态标志，避免状态漂移。
            if (_isDetailViewExpanded && !ViewModel.IsDetailViewMode)
            {
                _isDetailViewExpanded = false;
            }

            // 展开流程被中途重置（_isDetailViewExpanded 未落位或详细态已退出）时，
            // 动画可能已把 SizeToContent 冻结为 Manual 并写入了放大尺寸（Q3 竞态）：
            // 恢复自动尺寸，避免窗口停留在放大几何上。
            if (!_isDetailViewExpanded || !ViewModel.IsDetailViewMode)
            {
                SizeToContent = SizeToContent.WidthAndHeight;
                ClearValue(WidthProperty);
                ClearValue(HeightProperty);
            }
        }
    }

    /// <summary>
    /// 执行「标准平铺 → 放大详细视图」动画。返回 false 表示动画期间详细态被外部重置
    /// （如动画中关闭「详细功能」开关 → ClearDetailViewState 摘除动画并退出详细态），
    /// 此时不再回写放大几何，由调用方 finally 恢复尺寸一致性。
    /// </summary>
    private async Task<bool> AnimateToDetailViewAsync()
    {
        var startWidth = ActualWidth;
        var startHeight = ActualHeight;
        var layoutSettings = ViewModel.LayoutSettings;

        // 计算放大后的尺寸
        var targetWidth = layoutSettings.DetailListWidth
            + layoutSettings.DetailListPadding.Left + layoutSettings.DetailListPadding.Right + 12;
        var verticalPadding = layoutSettings.DetailListPadding.Top + layoutSettings.DetailListPadding.Bottom
            + layoutSettings.DetailListMargin.Top + layoutSettings.DetailListMargin.Bottom
            + ViewModel.HeaderRowHeight + 12;
        var targetHeight = ExpandAnimationHelper.CalculateExpandedHeight(
            ViewModel.Items.Count,
            layoutSettings.DetailListRowHeight,
            verticalPadding,
            GetWorkAreaDip().Height);

        // 冻结 SizeToContent，先设置当前尺寸，再切到详细态让 DetailList 可见。
        SizeToContent = SizeToContent.Manual;
        Width = startWidth;
        Height = startHeight;

        // 进入详细态：DetailList 显示（绑定 IsDetailViewMode），同时请求放大图标。
        ViewModel.SetDetailViewMode(true);
        DetailList.Opacity = 0;

        // 四象限定位
        var newPosition = ExpandAnimationHelper.CalculateExpandedPosition(
            Left, Top,
            _lastExpandClickPoint,
            new Size(startWidth, startHeight),
            new Size(targetWidth, targetHeight),
            GetWorkAreaDip());

        // 同步执行：窗口尺寸放大 + 位置移动 + 详细列表淡入
        var duration = TimeSpan.FromMilliseconds(200);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sizeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var widthAnim = new DoubleAnimation(startWidth, targetWidth, duration) { EasingFunction = easing };
        var heightAnim = new DoubleAnimation(startHeight, targetHeight, duration) { EasingFunction = easing };
        heightAnim.Completed += (_, _) => sizeCompletion.TrySetResult();
        BeginAnimation(WidthProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnim, HandoffBehavior.SnapshotAndReplace);

        var posCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leftAnim = new DoubleAnimation(Left, newPosition.X, duration) { EasingFunction = easing };
        var topAnim = new DoubleAnimation(Top, newPosition.Y, duration) { EasingFunction = easing };
        topAnim.Completed += (_, _) => posCompletion.TrySetResult();
        BeginAnimation(LeftProperty, leftAnim, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnim, HandoffBehavior.SnapshotAndReplace);

        DetailList.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = easing
            });

        // 动画被外部移除（如展开动画中关闭「详细功能」开关 → ClearDetailViewState 摘除动画）
        // 时 Completed 不触发，必须超时兜底，否则 await 永久挂起、finally 不执行，
        // _isDetailAnimating 卡 true 会导致之后所有展开/收缩被拒（无法打开）。
        await Task.WhenAny(
            Task.WhenAll(sizeCompletion.Task, posCompletion.Task),
            Task.Delay(TimeSpan.FromSeconds(1)));

        // 动画期间详细态已被外部重置（ClearDetailViewState 已摘除动画并退出详细态）：
        // 不再回写放大几何，避免 1s 超时后把窗口强制放大（Q3 竞态）。
        if (!ViewModel.IsDetailViewMode)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            DetailList.BeginAnimation(OpacityProperty, null);
            DetailList.Opacity = 1;
            return false;
        }

        // 清理动画
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        DetailList.BeginAnimation(OpacityProperty, null);
        DetailList.Opacity = 1;
        Width = targetWidth;
        Height = targetHeight;
        Left = newPosition.X;
        Top = newPosition.Y;
        return true;
    }

    internal async Task CollapseDetailViewAsync()
    {
        if (!_isDetailViewExpanded || _isDetailAnimating)
        {
            return;
        }

        _isDetailAnimating = true;
        try
        {
            _isDetailViewExpanded = false;

            var startWidth = ActualWidth;
            var startHeight = ActualHeight;
            var startLeft = Left;
            var startTop = Top;

            // 退出详细态（DetailList 隐藏，图标尺寸回到网格档）。
            ViewModel.SetDetailViewMode(false);

            // 恢复展开前的视图模式（而不是强制网格），避免改写用户持久化偏好。
            var restoreListMode = _expandOriginMode ?? false;
            await (restoreListMode
                ? ViewModel.UseMappingListModeCommand.ExecuteAsync(null)
                : ViewModel.UseMappingGridModeCommand.ExecuteAsync(null));

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);

            WindowBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var targetWidth = Math.Max(MinWidth, WindowBorder.DesiredSize.Width);
            var targetHeight = Math.Max(MinHeight, WindowBorder.DesiredSize.Height);

            SizeToContent = SizeToContent.Manual;
            Width = startWidth;
            Height = startHeight;

            // 收缩动画（300ms 直接回网格），同时回到展开前的原始位置。
            var duration = TimeSpan.FromMilliseconds(300);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sizeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var widthAnim = new DoubleAnimation(startWidth, targetWidth, duration) { EasingFunction = easing };
            var heightAnim = new DoubleAnimation(startHeight, targetHeight, duration) { EasingFunction = easing };
            heightAnim.Completed += (_, _) => sizeCompletion.TrySetResult();
            BeginAnimation(WidthProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
            BeginAnimation(HeightProperty, heightAnim, HandoffBehavior.SnapshotAndReplace);

            if (_expandOriginPosition is { } origin)
            {
                var posCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var leftAnim = new DoubleAnimation(startLeft, origin.X, duration) { EasingFunction = easing };
                var topAnim = new DoubleAnimation(startTop, origin.Y, duration) { EasingFunction = easing };
                topAnim.Completed += (_, _) => posCompletion.TrySetResult();
                BeginAnimation(LeftProperty, leftAnim, HandoffBehavior.SnapshotAndReplace);
                BeginAnimation(TopProperty, topAnim, HandoffBehavior.SnapshotAndReplace);
                await Task.WhenAny(
                    Task.WhenAll(sizeCompletion.Task, posCompletion.Task),
                    Task.Delay(TimeSpan.FromSeconds(1)));
                BeginAnimation(LeftProperty, null);
                BeginAnimation(TopProperty, null);
                Left = origin.X;
                Top = origin.Y;
            }
            else
            {
                await Task.WhenAny(sizeCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            }

            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            _expandOriginPosition = null;
            _expandOriginSize = null;
            _expandOriginMode = null;
        }
        finally
        {
            _isDetailAnimating = false;
            // 收缩动画期间被重新展开/重置的极端情况：以 ViewModel 详细态为准对齐标志。
            if (!_isDetailViewExpanded && ViewModel.IsDetailViewMode)
            {
                ViewModel.SetDetailViewMode(false);
            }
            QueueSendToBottom();
        }
    }

    private void OnDetailListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            _ = ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
            return;
        }

        // 双击详细视图空白处 → 收缩
        _ = CollapseDetailViewAsync();
    }

    /// <summary>
    /// 「详细功能」开关被关闭：若当前正处于详细态，走正常收缩动画回到展开前状态。
    /// 动画进行中（_isDetailAnimating）时先强制复位状态再收缩，避免状态字段不一致卡死。
    /// </summary>
    private void OnDetailExpandDisabled(object? sender, EventArgs e)
    {
        if (!_isDetailViewExpanded)
        {
            return;
        }

        if (_isDetailAnimating)
        {
            ClearDetailViewState();
            return;
        }

        _ = CollapseDetailViewAsync();
    }

    private void ClearDetailViewState()
    {
        _isDetailViewExpanded = false;
        _isDetailAnimating = false;        // 防止动画异常中断后永久卡 true
        _expandOriginPosition = null;
        _expandOriginSize = null;
        _expandOriginMode = null;
        if (ViewModel.IsDetailViewMode)
        {
            ViewModel.SetDetailViewMode(false);
        }
        SizeToContent = SizeToContent.WidthAndHeight; // 退出详细动画冻结的 Manual
        BeginAnimation(WidthProperty, null);          // 摘掉可能残留的窗口动画
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
    }

    // ---- 映射盒「详细功能」：拖拽交换 ----

    public async Task<bool> SwapItemsAsync(DrawerItemViewModel source, DrawerItemViewModel target)
    {
        if (source.Id == target.Id || !ViewModel.IsFreeSort)
        {
            return false;
        }

        // 记录原始位置（用于回滚）
        var sourceOriginal = (source.GridColumn, source.GridRow);
        var targetOriginal = (target.GridColumn, target.GridRow);

        // 内存交换
        source.SetGridPosition(targetOriginal.GridColumn, targetOriginal.GridRow, ViewModel.LayoutSettings);
        target.SetGridPosition(sourceOriginal.GridColumn, sourceOriginal.GridRow, ViewModel.LayoutSettings);

        // 持久化
        try
        {
            await ViewModel.UpdateSwapPositionsAsync(source, target);
        }
        catch
        {
            // 回滚内存
            source.SetGridPosition(sourceOriginal.GridColumn, sourceOriginal.GridRow, ViewModel.LayoutSettings);
            target.SetGridPosition(targetOriginal.GridColumn, targetOriginal.GridRow, ViewModel.LayoutSettings);
            // 单事务下正常不会出现半提交；补偿写仅兜底（best-effort）。
            try
            {
                await ViewModel.UpdateSwapPositionsAsync(source, target);
            }
            catch (Exception compensationException)
            {
                _logger.Error(compensationException, "Failed to persist rollback positions after swap failure.");
            }
            throw;
        }

        return true;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // 紧跟 DragLeave 的 DragOver 说明只是 resize churn：取消待执行的复位。
        CancelPendingDragLeaveReset();

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
        }

        ViewModel.IsDragOver = acceptsDrop;

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        // SizeToContent 窗口随拖拽预览在指针下方生长时，OLE 会补发 DragLeave/DragEnter 对
        // （churn）。若在此同步复位，就会出现"复位→下一帧 DragOver 再显示→再复位"的疯狂频闪。
        // 改为延迟复位：churn 场景紧跟的 DragOver 会取消它；真正离开/取消时没有后续
        // DragOver，复位在极短延迟后生效（肉眼不可辨）。
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _dragLeaveResetCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _ = ResetDragVisualStateAfterSettlingAsync(cts);
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
                    _ = CompleteInternalDropAsync(payload, slot.Value);
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

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 映射盒「详细功能」：Esc 关闭详细视图
        if (e.Key == Key.Escape && ViewModel.IsDetailExpandActive && _isDetailViewExpanded)
        {
            e.Handled = true;
            await CollapseDetailViewAsync();
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

    private bool IsSurfaceMouseIgnoredSource(object? source)
    {
        if (ViewModel.IsDrawerCollapsed || source is not DependencyObject dependencyObject)
        {
            return true;
        }

        return TryGetDrawerItem(source, out _)
            || FindVisualAncestor<Button>(dependencyObject) is not null
            || FindVisualAncestor<ScrollBar>(dependencyObject) is not null
            || FindVisualAncestor<Thumb>(dependencyObject) is not null
            || FindVisualAncestor<TextBox>(dependencyObject) is not null;
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _surfaceDragStartPoint = null;
        _surfaceDragStarted = false;
        _detailExpansionTriggeredOnMouseDown = false;

        if (IsSurfaceMouseIgnoredSource(e.OriginalSource))
        {
            return;
        }

        ClearItemSelection();

        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.ClickCount >= 2 && TryStartDetailExpansion(e))
        {
            _detailExpansionTriggeredOnMouseDown = true;
            e.Handled = true;
            return;
        }

        if (_isPositionLocked)
        {
            return;
        }

        // 详细态（放大预览）下窗口固定，禁用拖拽移动。
        if (_isDetailViewExpanded)
        {
            return;
        }

        // 开启「详细功能」的映射盒（网格态）：不立即 DragMove——DragMove 是系统级模态拖窗循环，
        // 会捕获鼠标吞掉随后的 MouseLeftButtonUp，导致"单击空白展开"永远收不到 Up。
        // 改为记录起点，由 OnWindowMouseMove 在移动超过阈值后才 DragMove；
        // 按下不动、松开时由 OnSurfaceMouseLeftButtonUp 触发展开预览。
        // 注意：不要在此处 CaptureMouse()——手动捕获后再 DragMove（内部模态消息循环）
        // 会因捕获冲突导致循环无法退出、UI 线程永久冻结（进程卡死）。拖动结束由
        // OnWindowMouseMove 的 finally 复位 _surfaceDragStarted 兜底（见 HANDOFF 4.1）。
        if (ViewModel.IsDetailExpandActive)
        {
            _surfaceDragStartPoint = e.GetPosition(this);
            return;
        }

        // 其他盒型：保持按下即拖窗口。
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
    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        // 开启「详细功能」的映射盒（网格态）：按下空白后移动超过阈值 → 开始拖窗口。
        if (_surfaceDragStartPoint is not { } start
            || _surfaceDragStarted
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < 4 && Math.Abs(current.Y - start.Y) < 4)
        {
            return;
        }

        _surfaceDragStarted = true;
        try
        {
            // DragMove 是系统级模态消息循环：阻塞 UI 线程直到鼠标松开（窗口内外均可结束）。
            // 松开时 MouseUp 事件与模态循环退出在同一消息处理内触发——OnSurfaceMouseLeftButtonUp
            // 执行时 _surfaceDragStarted 仍为 true，会走「拖动结束不展开」分支。
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
        finally
        {
            // DragMove 返回 = 拖动结束（无论鼠标在窗口内/外松开）。立即复位拖拽跟踪状态，
            // 避免 _surfaceDragStarted 卡 true 导致之后所有空白单击/双击被拒（无法展开，
            // HANDOFF 4.1）。手动 CaptureMouse 方案会与 DragMove 捕获冲突卡死 UI，故不采用。
            _surfaceDragStarted = false;
            _surfaceDragStartPoint = null;
        }
    }

    private void OnSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 释放按下时对空白区域的鼠标捕获（未捕获时无操作）。
        // 必须在任何 return 之前执行：捕获若不释放，后续鼠标事件会一直被路由到本窗口。
        if (IsSurfaceMouseIgnoredSource(e.OriginalSource))
        {
            return;
        }
        ReleaseMouseCapture();

        if (_detailExpansionTriggeredOnMouseDown)
        {
            _detailExpansionTriggeredOnMouseDown = false;
            return;
        }

        // 发生了拖动（窗口已移动）则不触发展开，仅复位拖拽跟踪状态。
        if (_surfaceDragStarted)
        {
            _surfaceDragStartPoint = null;
            _surfaceDragStarted = false;
            if (ViewModel.IsDetailExpandActive)
            {
                // 诊断：拖拽结束后的一次松开（含窗口外松开被捕获路由回的情况）。
                _logger.Info($"Box {ViewModel.BoxId}: blank release treated as drag end, expand skipped.");
            }

            return;
        }
        _surfaceDragStartPoint = null;

        if (TryStartDetailExpansion(e))
        {
            e.Handled = true;
        }
    }

    private bool TryStartDetailExpansion(MouseButtonEventArgs e)
    {
        if (!ShouldExpandDetailView(
                ViewModel.IsDetailClickToOpen,
                ViewModel.IsDetailDoubleClickToOpen,
                e.ClickCount,
                ViewModel.IsMappingListMode,
                _isDetailViewExpanded,
                _isDetailAnimating))
        {
            return false;
        }

        _lastExpandClickPoint = e.GetPosition(this);
        _logger.Info(
            $"Box {ViewModel.BoxId}: expand trigger clickCount={e.ClickCount} "
            + $"mode={(ViewModel.IsDetailOpenSingle ? "single" : "double")}.");
        _ = ExpandToDetailViewAsync();
        return true;
    }

    /// <summary>
    /// 空白点击是否应触发「详细视图」两级展开。
    /// 单击/双击模式由 <paramref name="clickCount"/> 与模式标志互斥判定：
    /// 单击模式 → 仅 ClickCount==1 展开；双击模式 → 仅 ClickCount&gt;=2 展开。
    /// 由表面鼠标按下/抬起处理器调用，供单测覆盖模式×点击组合
    /// （回归：双击模式下单击不得展开，见 review Q6）。
    /// </summary>
    internal static bool ShouldExpandDetailView(
        bool isClickToOpen,
        bool isDoubleClickToOpen,
        int clickCount,
        bool isListMode,
        bool isExpanded,
        bool isAnimating)
    {
        if (isListMode || isExpanded || isAnimating)
        {
            return false;
        }

        return clickCount switch
        {
            1 => isClickToOpen,
            >= 2 => isDoubleClickToOpen,
            _ => false
        };
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
        // 详细态（放大预览）下禁用图标拖拽。
        if (_isDetailViewExpanded)
        {
            ClearPendingIconDrag();
            return;
        }

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

        // 「详细功能」交换场景：落点是格位而非空格。TryGetAvailableDropSlot 会把占用格
        // 转成最近空格（为普通移动语义设计），导致交换分支永远拿不到占用格上的目标项；
        // 这里直接返回原始格位——占用格落放走交换，空格落放交换分支自然 miss、走普通落位。
        if (ViewModel.IsDetailExpandActive && ViewModel.IsFreeSort && movingItemId is not null)
        {
            return rawSlot;
        }

        // 固定模式（硬约束）：盒内找不到空位时返回 null，调用方据此拒绝拖放。
        return ViewModel.TryGetAvailableDropSlot(rawSlot.Column, rawSlot.Row, movingItemId, out var slot)
            ? slot
            : null;
    }

    private void ShowDropPreview(DragEventArgs e, DesktopBoxDragPayload? payload)
    {
        if (ViewModel.IsDrawerCollapsed)
        {
            var coverMovingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
            ShowDrawerCoverDropPreview(coverMovingItemId);
            return;
        }

        // 开启「详细功能」的映射盒：拖拽悬停时显示交换目标高亮（仅网格态；列表态下行内交换不落格位预览）。
        if (ViewModel.IsDetailExpandActive && ViewModel.IsFreeSort && !ViewModel.IsMappingListMode && payload?.SourceBoxId == ViewModel.BoxId)
        {
            var targetItem = GetItemAtDragPosition(e);
            if (targetItem is not null)
            {
                // 显示交换目标高亮（复用拖拽预览框架，定位到目标图标位置）
                ViewModel.ShowDragPreview(targetItem.GridColumn, targetItem.GridRow);
                return;
            }
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

    private DrawerItemViewModel? GetItemAtDragPosition(DragEventArgs e)
    {
        var itemList = ActiveItemsList;
        var point = e.GetPosition(itemList);
        var padding = itemList.Padding;
        var slot = ViewModel.GetGridSlot(
            point.X - padding.Left,
            point.Y - padding.Top,
            Math.Max(0, itemList.ActualWidth - padding.Left - padding.Right),
            Math.Max(0, itemList.ActualHeight - padding.Top - padding.Bottom));

        return ViewModel.Items.FirstOrDefault(
            item => item.GridColumn == slot.Column && item.GridRow == slot.Row);
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
            // 开启「详细功能」的映射盒：拖拽到已有图标位置 → 交换而非找空位
            if (ViewModel.IsDetailExpandActive && payload.SourceBoxId == ViewModel.BoxId)
            {
                var sourceItem = ViewModel.Items.FirstOrDefault(item => item.Id == payload.ItemId);
                var targetItem = ViewModel.Items.FirstOrDefault(
                    item => item.Id != payload.ItemId
                        && item.GridColumn == slot.Column
                        && item.GridRow == slot.Row);

                if (sourceItem is not null && targetItem is not null)
                {
                    if (await SwapItemsAsync(sourceItem, targetItem))
                    {
                        MarkDroppedInsideWitchDrawer(payload);
                        SelectItem(payload.ItemId);
                        moved = true;
                        return;
                    }
                    // 非自由排序：交换不可用。落到下方普通落位逻辑，
                    // 由 DropDrawerItemAsync 按排序键决定落点（盒内拖动为空操作）。
                }
            }

            moved = await ViewModel.DropDrawerItemAsync(payload.ItemId, slot.Column, slot.Row);
            if (moved)
            {
                MarkDroppedInsideWitchDrawer(payload);
                SelectItem(payload.ItemId);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to complete internal drop.");
            ViewModel.ReportDropFailure();
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
        // 立即复位（落放/拖拽结束/全局清理）：任何延迟复位都取消。
        CancelPendingDragLeaveReset();
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

    private static readonly nint WindowPositionTopmost = -1;
    private const int WindowOwnerIndex = -8;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoActivate = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private const uint MonitorDefaultToNearest = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    /// <summary>
    /// 窗口当前所在显示器的工作区（DIP）。<see cref="SystemParameters.WorkArea"/> 只覆盖主屏，
    /// 多显示器下必须按窗口所在屏取工作区，否则副屏上的盒子会被钳制逻辑误判越界搬回主屏。
    /// 句柄尚未创建或查询失败时回退到主屏工作区。
    /// </summary>
    internal Rect GetWorkAreaDip()
    {
        var fallback = SystemParameters.WorkArea;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return fallback;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return fallback;
        }

        var info = new NativeMonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return fallback;
        }

        // GetMonitorInfo 返回物理像素，按窗口当前 DPI 换算成 DIP。
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            info.WorkArea.Left / dpi.DpiScaleX,
            info.WorkArea.Top / dpi.DpiScaleY,
            (info.WorkArea.Right - info.WorkArea.Left) / dpi.DpiScaleX,
            (info.WorkArea.Bottom - info.WorkArea.Top) / dpi.DpiScaleY);
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
            ?? ItemsControl.ContainerFromElement(FileList, dependencyObject) as FrameworkElement
            ?? ItemsControl.ContainerFromElement(DetailList, dependencyObject) as FrameworkElement;
        if (container?.DataContext is not DrawerItemViewModel item)
        {
            return false;
        }

        drawerItem = item;
        return true;
    }
}
