using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    private const string BoxPositionSettingPrefix = "BoxPosition:";
    private const string PhysicalPositionPrefix = "px:";
    private const string LayoutBackupSettingPrefix = "LayoutBackup:";
    private const int LayoutBackupVersion = 2;
    private const int LegacyLayoutBackupVersion = 1;
    private const int LayoutBackupSlotCount = 3;
    private const int MaxLayoutBackupPositions = 4096;
    private const char PositionSeparator = ',';

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IShellChangeNotifier _shellChangeNotifier;
    private readonly IAppLogger _logger;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private readonly Dictionary<Guid, DesktopBoxWindow> _windows = [];
    private readonly ForegroundWindowMonitor _foregroundWindowMonitor;
    private readonly GlobalMouseButtonMonitor _mouseButtonMonitor;
    private readonly DesktopDoubleClickDetector _desktopDoubleClickDetector = new();
    private readonly Channel<DesktopMouseButtonEvent> _desktopMouseButtonEvents =
        Channel.CreateUnbounded<DesktopMouseButtonEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
    private readonly Task _desktopMouseButtonProcessor;
    private readonly Func<bool> _isDesktopDoubleClickEnabled;
    private readonly HashSet<Guid> _overlapResolutionBoxIds = [];
    private AutoHideSettings _autoHideSettings = AutoHideSettings.Defaults;
    private readonly HashSet<Guid> _autoHideHoveredBoxIds = [];
    private bool _closing;
    private bool _desktopIsForeground;
    private CancellationTokenSource? _foregroundChangeCts;
    private long _showDesktopShortcutObservedUntilTick;
    private GuideLineWindow? _verticalGuide;
    private GuideLineWindow? _horizontalGuide;
    private bool _isAdjustingPosition;

    public DesktopBoxManager(
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IShellChangeNotifier shellChangeNotifier,
        IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore,
        BoxPositionLockStateStore boxPositionLockStateStore,
        Func<bool> isDesktopDoubleClickEnabled)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _shellChangeNotifier = shellChangeNotifier;
        _logger = logger;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        _isDesktopDoubleClickEnabled = isDesktopDoubleClickEnabled;
        _foregroundWindowMonitor = new ForegroundWindowMonitor();
        _foregroundWindowMonitor.ForegroundWindowChanged += OnForegroundWindowChanged;
        _desktopIsForeground = ForegroundWindowMonitor.IsDesktopWindow(
            ForegroundWindowMonitor.GetCurrentForegroundWindow());
        if (!_foregroundWindowMonitor.IsActive)
        {
            _logger.Info("Foreground window monitoring is unavailable; Show Desktop layering may be limited.");
        }

        // 盒子带 WS_EX_NOACTIVATE，点击不激活窗口，桌面点击不会产生 Deactivated
        // 事件，选中框无法自动清除。全局鼠标钩子补上"外部点击"信号。
        _mouseButtonMonitor = new GlobalMouseButtonMonitor();
        _mouseButtonMonitor.MouseButtonDown += OnGlobalMouseButtonDown;
        _mouseButtonMonitor.MouseButtonPressed += OnGlobalMouseButtonPressed;
        // One background consumer preserves click order without blocking the WPF dispatcher.
        _desktopMouseButtonProcessor = Task.Run(ProcessDesktopMouseButtonEventsAsync);
        if (!_mouseButtonMonitor.IsActive)
        {
            _logger.Info("Global mouse monitoring is unavailable; outside clicks will not clear box selection.");
        }

        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxLayoutPresetChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxLayoutPreset(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxPositionLockStateChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxPositionLockState(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxTitleVisibilityChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyTitleVisibility(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxFileNameVisibilityChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyFileNameVisibility(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxHoverRollUpEnabledChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyHoverRollUpEnabled(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, DrawerSortModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyDrawerSortMode(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxSizeModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxSizeMode(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, AutoHideSettingsChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyAutoHideSettings(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, IconToolTipModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyIconToolTipMode(message));
    }

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;

    public event EventHandler? DesktopBackgroundDoubleClicked;

    public event EventHandler? ShowDesktopActivated;

    private int _refreshVersion;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    internal void InvalidateRefresh() => Interlocked.Increment(ref _refreshVersion);

    public async Task RefreshAsync(StartupSettingsSnapshot? startupSnapshot = null)
    {
        if (_closing)
        {
            return;
        }

        var version = Interlocked.Increment(ref _refreshVersion);
        await _refreshGate.WaitAsync();
        try
        {
            if (_closing || version != Volatile.Read(ref _refreshVersion))
            {
                return;
            }

            var boxes = await _drawerService.GetBoxesAsync();
            if (_closing || version != Volatile.Read(ref _refreshVersion))
            {
                return;
            }

            var boxIds = boxes.Select(box => box.Id).ToHashSet();

            // 每次刷新重新标记哪些窗口参与重叠消解：只有本次新放置（无存档位置）
            // 或落位时被工作区钳制过（分辨率/显示器变化）的窗口才可被挪动。
            _overlapResolutionBoxIds.Clear();

            foreach (var removedId in _windows.Keys.Where(id => !boxIds.Contains(id)).ToArray())
            {
                var win = _windows[removedId];
                win.LocationChanged -= OnWindowLocationChanged;
                win.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
                win.AutoHideHoverEntered -= OnWindowAutoHideHoverEntered;
                win.AutoHideHoverLeft -= OnWindowAutoHideHoverLeft;
                win.ForceClose();
                _windows.Remove(removedId);
            }

            for (var index = 0; index < boxes.Count; index++)
            {
                if (_closing || version != Volatile.Read(ref _refreshVersion))
                {
                    return;
                }

                var box = boxes[index];
                var visualStyle = await _boxVisualStyleStore.LoadAsync(
                    box,
                    startupSnapshot: startupSnapshot);
                var isPositionLocked =
                    await _boxPositionLockStateStore.LoadAsync(
                        box.Id,
                        startupSnapshot: startupSnapshot);
                var isNewWindow = false;
                if (!_windows.TryGetValue(box.Id, out var window))
                {
                    isNewWindow = true;
                    var layoutSettings = new DesktopBoxLayoutSettings(
                        box.Type == WitchDrawer.Core.Models.BoxType.Drawer);
                    var savedPreset = startupSnapshot is not null
                        ? startupSnapshot.Get(BoxViewModel.GetLayoutPresetSettingKey(box.Id))
                        : await _drawerService.GetSettingAsync(
                            BoxViewModel.GetLayoutPresetSettingKey(box.Id));
                    layoutSettings.ApplyPresetWithoutCallback(savedPreset);

                    var viewModel = new DesktopBoxViewModel(
                        box,
                        _drawerService,
                        _todoService,
                        _launcher,
                        _shellChangeNotifier,
                        _logger,
                        visualStyle,
                        layoutSettings);
                    await viewModel.LoadTitleVisibilityAsync(startupSnapshot);
                    await viewModel.LoadFileNameVisibilityAsync(startupSnapshot);
                    await viewModel.LoadMappingViewModeAsync(startupSnapshot);
                    await viewModel.LoadMappingListWidthAsync(startupSnapshot);
                    await viewModel.LoadTodoPanelSizeAsync(startupSnapshot);
                    // The persisted drawer height is snapped against the active row height.
                    // Load the file-name row first so a saved 4x4 cover stays 4x4 after restart.
                    await viewModel.LoadDrawerCoverSizeAsync(startupSnapshot);
                    await viewModel.LoadRollUpStateAsync(startupSnapshot);
                    await viewModel.LoadHoverRollUpEnabledAsync(startupSnapshot);
                    await viewModel.LoadSortModeAsync(startupSnapshot);
                    await viewModel.LoadSizeModeAsync(startupSnapshot);
                    viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(
                        this,
                        new BoxItemsChangedEventArgs(viewModel.BoxId));

                    window = new DesktopBoxWindow(viewModel);
                    var requiresOverlapResolution =
                        await PlaceWindowAsync(window, box.Id, index);

                    _windows.Add(box.Id, window);

                    window.LocationChanged += OnWindowLocationChanged;
                    window.PreviewMouseLeftButtonUp += OnWindowMouseUp;
                    window.AutoHideHoverEntered += OnWindowAutoHideHoverEntered;
                    window.AutoHideHoverLeft += OnWindowAutoHideHoverLeft;
                    window.ApplyAutoHideState(_autoHideSettings);
                    window.SetPositionChangedCallback(async (id) =>
                    {
                        _isAdjustingPosition = true;
                        try
                        {
                            PerformSnappingAndAlignment(window, applySnap: true);
                        }
                        finally
                        {
                            _isAdjustingPosition = false;
                        }
                        HideGuides();
                        await SavePositionAsync(id);
                    });

                    // 先创建句柄：OnSourceInitialized 里完成挂桌面 + 沉底，
                    // 窗口首次可见时就已经在桌面层，不会先浮在最上层闪一帧再被压回。
                    new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                    window.Show();
                    window.SetPositionLocked(isPositionLocked);
                    window.SetDesktopForeground(_desktopIsForeground);
                    window.QueueSendToBottom();
                    await window.ViewModel.LoadAsync();
                    // 首次布局的测量约束来自初始 HWND 尺寸，内容稳定后强制重测一次，
                    // 否则窗口会一直停在错误的初始宽度（折叠抽屉盒封面两侧突出）。
                    window.ResyncSizeToContent();
                    window.UpdateLayout();
                    if (ClampWindowToCurrentWorkArea(window))
                    {
                        requiresOverlapResolution = true;
                    }

                    // Startup SizeChanged events are based on provisional template sizes.
                    // Only future user/content-driven resizes may clamp the live window.
                    window.EnableVisibleBoundsClamping();
                    if (requiresOverlapResolution)
                    {
                        _overlapResolutionBoxIds.Add(box.Id);
                    }
                }
                else
                {
                    window.ViewModel.UpdateBox(box, visualStyle);
                    window.SetPositionLocked(isPositionLocked);
                }

                window.SetDesktopForeground(_desktopIsForeground);
                window.QueueSendToBottom();

                if (isNewWindow)
                {
                    // Each new WPF window builds a large visual tree. Render it and
                    // process input before constructing the next one. Existing-window
                    // refreshes have no such work and should complete without a yield.
                    await System.Windows.Threading.Dispatcher.Yield(
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            ResolveWindowOverlaps();
            if (DesktopToolWindow.RepairShellLastActivePopup())
            {
                _logger.Info("Reset Progman last-active-popup after attaching desktop boxes.");
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Reloads item lists for existing desktop windows without recreating them.
    /// </summary>
    public async Task RefreshItemsAsync(Guid? affectedBoxId = null)
    {
        if (_closing)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            if (affectedBoxId is Guid boxId)
            {
                if (_windows.TryGetValue(boxId, out var affectedWindow)
                    && affectedWindow.IsVisible)
                {
                    await affectedWindow.ViewModel.LoadAsync();
                }

                return;
            }

            foreach (var window in _windows.Values.Where(window => window.IsVisible).ToArray())
            {
                await window.ViewModel.LoadAsync();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Reattaches desktop boxes after Explorer recreates the Shell desktop.
    /// If Explorer destroyed an owned HWND, remove the stale WPF window and let
    /// the normal refresh path recreate it from persisted box data.
    /// </summary>
    public async Task RecoverDesktopHostsAsync()
    {
        if (_closing)
        {
            return;
        }

        // TaskbarCreated is broadcast as Explorer comes back. Give Progman a
        // short window to finish creating before resolving the new owner HWND.
        await Task.Delay(350);
        if (_closing)
        {
            return;
        }

        var recreateRequired = false;
        foreach (var (boxId, window) in _windows.ToArray())
        {
            if (window.IsNativeWindowAlive)
            {
                window.RefreshDesktopHost();
                window.QueueSendToBottom();
                continue;
            }

            window.LocationChanged -= OnWindowLocationChanged;
            window.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
            // 外部（Explorer 重建桌面）销毁 HWND 不会触发 WPF Closed，必须显式 ForceClose
            // 让 OnClosed 里的退订/清理执行，否则整棵窗口对象图被静态事件永久引用（僵尸泄漏）。
            window.ForceClose();
            _windows.Remove(boxId);
            recreateRequired = true;
        }

        if (recreateRequired)
        {
            await RefreshAsync();
        }

        if (DesktopToolWindow.RepairShellLastActivePopup())
        {
            _logger.Info("Reset Progman last-active-popup after recovering desktop hosts.");
        }
    }

    /// <summary>
    /// Reopens the desktop window for a box that was hidden via its close (X)
    /// button. If the window still exists in memory it is simply shown again;
    /// otherwise a full refresh is triggered so it gets recreated.
    /// </summary>
    /// <returns><see langword="true"/> if a window was shown; <see langword="false"/> otherwise.</returns>
    public async Task<bool> ShowAsync(Guid boxId)
    {
        if (_closing)
        {
            return false;
        }

        if (_windows.TryGetValue(boxId, out var window))
        {
            if (!window.IsVisible)
            {
                await window.ViewModel.LoadAsync();
                window.Show();
            }

            window.QueueSendToBottom();
            return true;
        }

        // Window was destroyed (e.g. fully closed) or never created this session:
        // refresh so the box window is recreated for the current box set.
        await RefreshAsync();
        return _windows.TryGetValue(boxId, out var refreshed) && refreshed.IsVisible;
    }

    public async Task ShowAllAsync()
    {
        if (_closing)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            var hiddenWindows = SnapshotHiddenWindows(
                _windows.Values,
                static window => window.IsVisible);
            foreach (var window in hiddenWindows)
            {
                await window.ViewModel.LoadAsync();
                if (_closing)
                {
                    return;
                }

                window.Show();
                window.QueueSendToBottom();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal static TWindow[] SnapshotHiddenWindows<TWindow>(
        IEnumerable<TWindow> windows,
        Func<TWindow, bool> isVisible)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(isVisible);
        return windows.Where(window => !isVisible(window)).ToArray();
    }

    public async Task CloseAllAsync()
    {
        _closing = true;
        await SaveAllPositionsAsync();
        foreach (var window in _windows.Values)
        {
            window.LocationChanged -= OnWindowLocationChanged;
            window.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
            window.AutoHideHoverEntered -= OnWindowAutoHideHoverEntered;
            window.AutoHideHoverLeft -= OnWindowAutoHideHoverLeft;
            window.ForceClose();
        }

        _windows.Clear();
        var foregroundChangeCts = Interlocked.Exchange(ref _foregroundChangeCts, null);
        foregroundChangeCts?.Cancel();
        foregroundChangeCts?.Dispose();
        _foregroundWindowMonitor.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _foregroundWindowMonitor.Dispose();
        _mouseButtonMonitor.MouseButtonDown -= OnGlobalMouseButtonDown;
        _mouseButtonMonitor.MouseButtonPressed -= OnGlobalMouseButtonPressed;
        _mouseButtonMonitor.Dispose();
        _desktopMouseButtonEvents.Writer.TryComplete();
        await _desktopMouseButtonProcessor;

        _verticalGuide?.Close();
        _verticalGuide = null;
        _horizontalGuide?.Close();
        _horizontalGuide = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private readonly record struct DesktopMouseButtonEvent(
        int ScreenX,
        int ScreenY,
        uint Timestamp,
        GlobalMouseButton Button,
        bool IsDesktopDoubleClickEnabled);

    internal readonly record struct LayoutBackupPosition(
        Guid BoxId,
        double Left,
        double Top,
        bool IsPhysicalPixels = false);

    public readonly record struct LayoutBackupRestoreResult(
        bool BackupFound,
        int RestoredCount,
        int MissingCount);

    private sealed record LayoutBackupPayload(int Version, LayoutBackupPosition[] Positions);

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isAdjustingPosition || _closing)
        {
            return;
        }

        if (sender is not DesktopBoxWindow draggedWindow)
        {
            return;
        }

        if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            HideGuides();
            return;
        }

        _isAdjustingPosition = true;
        try
        {
            PerformSnappingAndAlignment(draggedWindow, applySnap: false);
        }
        finally
        {
            _isAdjustingPosition = false;
        }
    }

    private void OnWindowMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HideGuides();
        if (sender is DesktopBoxWindow window)
        {
            FireAndForget.Run(
                SavePositionAsync(window.ViewModel.BoxId),
                _logger,
                $"Failed to save position for box {window.ViewModel.BoxId:N}.");
        }
    }

}
