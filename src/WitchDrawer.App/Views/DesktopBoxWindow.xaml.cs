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
    private bool _isDrawerResizing;
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
        _itemContextMenu = new DrawerItemContextMenuCoordinator(viewModel, ownerVisible: () => IsVisible);
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

    private ListBox ActiveItemsList => ViewModel.IsMappingListMode ? FileList : IconList;

    public void ForceClose()
    {
        _itemContextMenu.CloseActiveMenu();
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _itemContextMenu.CloseActiveMenu();
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
        System.Windows.Media.CompositionTarget.Rendering -= OnPendingAppearanceFrame;
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
        if (DesktopWindowLayer.IsEnabled)
        {
            DesktopWindowLayer.Configure(handle);
        }
        else
        {
            _nativeWindow.Configure();
        }
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private CancellationTokenSource? _dragLeaveResetCts;


}
