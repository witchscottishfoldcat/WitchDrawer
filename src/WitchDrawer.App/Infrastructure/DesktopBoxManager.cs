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

public sealed class DesktopBoxManager
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
    private bool _closing;
    private bool _desktopIsForeground;
    private CancellationTokenSource? _foregroundChangeCts;
    private GuideLineWindow? _verticalGuide;
    private GuideLineWindow? _horizontalGuide;
    private bool _isAdjustingPosition;

    public DesktopBoxManager(
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore,
        BoxPositionLockStateStore boxPositionLockStateStore,
        Func<bool> isDesktopDoubleClickEnabled)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
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
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, DrawerSortModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyDrawerSortMode(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxSizeModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxSizeMode(message));
    }

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;

    public event EventHandler? DesktopBackgroundDoubleClicked;

    private int _refreshVersion;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public async Task RefreshAsync()
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
                var visualStyle = await _boxVisualStyleStore.LoadAsync(box);
                var isPositionLocked =
                    await _boxPositionLockStateStore.LoadAsync(box.Id);
                if (!_windows.TryGetValue(box.Id, out var window))
                {
                    var layoutSettings = new DesktopBoxLayoutSettings(
                        box.Type == WitchDrawer.Core.Models.BoxType.Drawer);
                    var savedPreset = await _drawerService.GetSettingAsync(
                        BoxViewModel.GetLayoutPresetSettingKey(box.Id));
                    layoutSettings.ApplyPresetWithoutCallback(savedPreset);

                    var viewModel = new DesktopBoxViewModel(
                        box,
                        _drawerService,
                        _todoService,
                        _launcher,
                        _logger,
                        visualStyle,
                        layoutSettings);
                    await viewModel.LoadTitleVisibilityAsync();
                    await viewModel.LoadFileNameVisibilityAsync();
                    // The persisted drawer height is snapped against the active row height.
                    // Load the file-name row first so a saved 4x4 cover stays 4x4 after restart.
                    await viewModel.LoadDrawerCoverSizeAsync();
                    await viewModel.LoadRollUpStateAsync();
                    await viewModel.LoadSortModeAsync();
                    await viewModel.LoadSizeModeAsync();
                    viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(
                        this,
                        new BoxItemsChangedEventArgs(viewModel.BoxId));

                    window = new DesktopBoxWindow(viewModel);
                    if (await PlaceWindowAsync(window, box.Id, index))
                    {
                        _overlapResolutionBoxIds.Add(box.Id);
                    }

                    _windows.Add(box.Id, window);

                    window.LocationChanged += OnWindowLocationChanged;
                    window.PreviewMouseLeftButtonUp += OnWindowMouseUp;
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
                }
                else
                {
                    window.ViewModel.UpdateBox(box, visualStyle);
                    window.SetPositionLocked(isPositionLocked);
                }

                window.SetDesktopForeground(_desktopIsForeground);
                window.QueueSendToBottom();
            }

            ResolveWindowOverlaps();
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

    public async Task SaveAllPositionsAsync()
    {
        var positions = _windows
            .Select(pair => new KeyValuePair<string, string>(
                BoxPositionSettingPrefix + pair.Key.ToString("N"),
                CaptureStoredPosition(pair.Value)))
            .ToArray();

        foreach (var (key, value) in positions)
        {
            await _drawerService.SetSettingAsync(key, value);
        }

        _logger.Info($"Recorded complete desktop layout for {positions.Length} boxes.");
    }

    public async Task<int> RecordLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var positions = _windows
            .Select(pair => CaptureLayoutBackupPosition(pair.Key, pair.Value))
            .ToArray();
        var value = SerializeLayoutBackup(positions);

        await _drawerService.SetSettingAsync(key, value);
        _logger.Info($"Recorded layout backup slot {slot} with {positions.Length} boxes.");
        return positions.Length;
    }

    public async Task<bool> HasLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var value = await _drawerService.GetSettingAsync(key);
        var hasBackup = TryParseLayoutBackup(value, out _);
        if (!hasBackup && !string.IsNullOrWhiteSpace(value))
        {
            _logger.Info($"Layout backup slot {slot} contains invalid data and is treated as empty.");
        }

        return hasBackup;
    }

    public async Task<bool> DeleteLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var deleted = await _drawerService.DeleteSettingAsync(key);
        _logger.Info(
            deleted
                ? $"Deleted layout backup slot {slot}."
                : $"Layout backup slot {slot} was already empty when deletion was requested.");
        return deleted;
    }

    public async Task<LayoutBackupRestoreResult> RestoreLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        await _refreshGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return new LayoutBackupRestoreResult(false, 0, 0);
            }

            var value = await _drawerService.GetSettingAsync(key);
            if (!TryParseLayoutBackup(value, out var positions))
            {
                _logger.Info($"Layout backup slot {slot} is empty or invalid; restore skipped.");
                return new LayoutBackupRestoreResult(false, 0, 0);
            }

            var restoredCount = 0;
            var missingCount = 0;
            _isAdjustingPosition = true;
            try
            {
                foreach (var position in positions)
                {
                    if (!_windows.TryGetValue(position.BoxId, out var window))
                    {
                        missingCount++;
                        continue;
                    }

                    if (position.IsPhysicalPixels)
                    {
                        window.MoveWindowOriginPixels(position.Left, position.Top);
                    }
                    else
                    {
                        // Version 1 backups stored WPF DIPs without monitor identity.
                        // Keep the legacy interpretation for one-time migration.
                        window.Left = position.Left;
                        window.Top = position.Top;
                    }
                    window.ResyncSizeToContent();
                    window.UpdateLayout();

                    var bounds = window.GetVisibleBoundsPixels();
                    var workArea = window.GetWorkAreaPixels();
                    if (bounds.Width > 0 && bounds.Height > 0 && !workArea.IsEmpty)
                    {
                        var origin = CalculateClampedVisibleOrigin(bounds, workArea);
                        window.MoveToVisibleOriginPixels(origin.X, origin.Y);
                    }

                    if (window.IsVisible)
                    {
                        window.QueueSendToBottom();
                    }

                    restoredCount++;
                }
            }
            finally
            {
                _isAdjustingPosition = false;
            }

            await SaveAllPositionsAsync();
            _logger.Info(
                $"Restored layout backup slot {slot}: restored={restoredCount}, missing={missingCount}.");
            return new LayoutBackupRestoreResult(true, restoredCount, missingCount);
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

    public async Task SavePositionAsync(Guid boxId)
    {
        if (!_windows.TryGetValue(boxId, out var window))
        {
            return;
        }

        var key = BoxPositionSettingPrefix + boxId.ToString("N");
        var value = CaptureStoredPosition(window);
        await _drawerService.SetSettingAsync(key, value);
    }

    public async Task<bool> CenterBoxOnScreenAsync(Guid boxId)
    {
        if (_closing)
        {
            return false;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (_closing || !_windows.TryGetValue(boxId, out var window))
            {
                _logger.Info($"Skipped screen-center recall for missing desktop box {boxId:N}.");
                return false;
            }

            if (!window.IsVisible)
            {
                await window.ViewModel.LoadAsync();
                if (_closing)
                {
                    return false;
                }

                window.Show();
            }

            window.ResyncSizeToContent();
            window.UpdateLayout();
            var bounds = window.GetVisibleBoundsPixels();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                _logger.Info($"Skipped screen-center recall for unmeasured desktop box {boxId:N}.");
                return false;
            }

            var workArea = DesktopBoxWindow.GetPrimaryWorkAreaPixels();
            if (workArea.IsEmpty)
            {
                _logger.Info($"Skipped screen-center recall because the primary work area is unavailable for {boxId:N}.");
                return false;
            }

            var origin = CalculateCenteredVisibleOrigin(bounds.Size, workArea);
            _isAdjustingPosition = true;
            try
            {
                window.MoveToVisibleOriginPixels(origin.X, origin.Y);
            }
            finally
            {
                _isAdjustingPosition = false;
            }

            window.QueueSendToBottom();
            var key = BoxPositionSettingPrefix + boxId.ToString("N");
            var value = CaptureStoredPosition(window);
            await _drawerService.SetSettingAsync(key, value);
            _logger.Info($"Recalled desktop box {boxId:N} to screen center at {value}.");
            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task CloseAllAsync()
    {
        _closing = true;
        await SaveAllPositionsAsync();
        foreach (var window in _windows.Values)
        {
            window.LocationChanged -= OnWindowLocationChanged;
            window.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
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

    /// <summary>
    /// 盒子带 WS_EX_NOACTIVATE：点击盒子不激活任何窗口，随后点击桌面时
    /// Window/Application.Deactivated 都不会触发，选中框（蓝框）会一直残留。
    /// 全局鼠标钩子在每次按键时命中测试光标下的窗口：命中点不在某个盒子的
    /// 窗口上，就清掉那个盒子的选中态。命中本盒子时保留——本盒子自己的鼠标
    /// 处理（点空白清空/点项目改选）会接着处理这次点击。
    /// </summary>
    internal static bool ShouldClearSelectionOnOutsideClick(nint clickedHandle, nint boxHandle) =>
        clickedHandle != boxHandle;

    private void OnGlobalMouseButtonDown(int screenX, int screenY)
    {
        if (_closing)
        {
            return;
        }

        var clickedHandle = GlobalMouseButtonMonitor.HitTestWindowHandle(screenX, screenY);
        foreach (var window in _windows.Values)
        {
            if (ShouldClearSelectionOnOutsideClick(clickedHandle, window.NativeHandle))
            {
                window.ClearSelectionFromOutside();
            }
        }
    }

    private void OnGlobalMouseButtonPressed(
        int screenX,
        int screenY,
        uint timestamp,
        GlobalMouseButton button)
    {
        if (_closing)
        {
            return;
        }

        _desktopMouseButtonEvents.Writer.TryWrite(new DesktopMouseButtonEvent(
            screenX,
            screenY,
            timestamp,
            button,
            _isDesktopDoubleClickEnabled()));
    }

    private async Task ProcessDesktopMouseButtonEventsAsync()
    {
        await foreach (var mouseEvent in _desktopMouseButtonEvents.Reader.ReadAllAsync()
                           .ConfigureAwait(false))
        {
            if (_closing || !mouseEvent.IsDesktopDoubleClickEnabled)
            {
                _desktopDoubleClickDetector.Reset();
                continue;
            }

            bool isBlankDesktopPoint;
            try
            {
                isBlankDesktopPoint = mouseEvent.Button == GlobalMouseButton.Left
                    && DesktopIconVisibility.IsBlankDesktopPoint(
                        mouseEvent.ScreenX,
                        mouseEvent.ScreenY);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Failed to test the desktop click target.");
                _desktopDoubleClickDetector.Reset();
                continue;
            }

            if (!_desktopDoubleClickDetector.RegisterButtonDown(
                    mouseEvent.ScreenX,
                    mouseEvent.ScreenY,
                    mouseEvent.Timestamp,
                    mouseEvent.Button,
                    isBlankDesktopPoint))
            {
                continue;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                continue;
            }

            _ = dispatcher.BeginInvoke(() =>
            {
                if (!_closing && _isDesktopDoubleClickEnabled())
                {
                    DesktopBackgroundDoubleClicked?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }

    private void OnForegroundWindowChanged(nint windowHandle)
    {
        if (_closing)
        {
            return;
        }

        // Win+D emits a short burst of foreground changes (for example Progman,
        // WorkerW and transient shell windows). Applying every intermediate handle
        // moves all boxes up and down several times and produces a visible flash.
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _foregroundChangeCts, next);
        previous?.Cancel();
        previous?.Dispose();
        FireAndForget.Run(
                ApplyForegroundWindowAfterSettlingAsync(next),
                _logger,
                "Failed to apply foreground window state after settling.");
    }

    private async Task ApplyForegroundWindowAfterSettlingAsync(CancellationTokenSource changeCts)
    {
        var cancellationToken = changeCts.Token;
        try
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _closing))
            {
                return;
            }

            var windowHandle = ForegroundWindowMonitor.GetCurrentForegroundWindow();
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ApplyForegroundWindow(windowHandle);
                    }
                },
                System.Windows.Threading.DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _foregroundChangeCts, null, changeCts),
                    changeCts))
            {
                changeCts.Dispose();
            }
        }
    }

    private readonly record struct DesktopMouseButtonEvent(
        int ScreenX,
        int ScreenY,
        uint Timestamp,
        GlobalMouseButton Button,
        bool IsDesktopDoubleClickEnabled);

    private void ApplyForegroundWindow(nint windowHandle)
    {
        if (_closing || windowHandle == nint.Zero)
        {
            return;
        }

        var isDesktopWindow = ForegroundWindowMonitor.IsDesktopWindow(windowHandle);
        var isDesktopBoxWindow = _windows.Values.Any(
            window => window.NativeHandle == windowHandle);
        SetDesktopForeground(ResolveDesktopForegroundState(
            isDesktopWindow,
            isDesktopBoxWindow));
    }


    internal static bool ResolveDesktopForegroundState(
        bool isDesktopWindow,
        bool isDesktopBoxWindow) =>
        isDesktopWindow && !isDesktopBoxWindow;

    private void SetDesktopForeground(bool isForeground)
    {
        if (_desktopIsForeground == isForeground)
        {
            return;
        }

        _desktopIsForeground = isForeground;
        foreach (var window in _windows.Values)
        {
            window.SetDesktopForeground(isForeground);
        }
    }

    private void ApplyBoxLayoutPreset(BoxLayoutPresetChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.ViewModel.LayoutSettings.ApplyPresetWithoutCallback(message.Preset);
    }

    private void ApplyBoxSizeMode(BoxSizeModeChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.ViewModel.ApplySizeMode(
            new BoxSizeModeState(message.IsFixed, message.Columns, message.Rows));
    }

    private void ApplyBoxPositionLockState(
        BoxPositionLockStateChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.SetPositionLocked(message.IsPositionLocked);
        _logger.Info(
            $"Applied position lock state {message.IsPositionLocked} "
            + $"to desktop box {message.BoxId:N}.");
    }

    private void ApplyTitleVisibility(
        BoxTitleVisibilityChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyTitleVisibility(message.IsVisible);
        }
    }

    private void ApplyFileNameVisibility(
        BoxFileNameVisibilityChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyFileNameVisibility(message.IsVisible);
            if (window.ViewModel.IsDrawerBox)
            {
                FireAndForget.Run(
                    window.ViewModel.SaveDrawerCoverSizeAsync(),
                    _logger,
                    $"Failed to save resized drawer cover for box {message.BoxId:N}.");
            }
        }
    }

    private void ApplyDrawerSortMode(DrawerSortModeChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window)
            && window.ViewModel.ApplyDrawerSortMode(message.SortMode))
        {
            // 排序模式变化：重排盒内显示（自由模式则从 DB 恢复记忆布局）。
            _ = window.ViewModel.LoadAsync();
        }
    }

    /// <summary>
    /// 恢复窗口位置。返回 <see langword="true"/> 表示该窗口需要参与重叠消解：
    /// 没有存档位置的新盒子（按级联位落位，可能压住已有盒子），或存档位置被
    /// 工作区钳制过（分辨率/显示器变化导致多个盒子挤到同一边缘）。原样还原的
    /// 盒子返回 <see langword="false"/>——保持用户摆好的相对关系，哪怕彼此重叠。
    /// </summary>
    private async Task<bool> PlaceWindowAsync(DesktopBoxWindow window, Guid boxId, int fallbackIndex)
    {
        // SizeToContent windows report NaN for Width/Height before they are shown; measure
        // first and use DesiredSize so saved positions are restored correctly.
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var savedPosition = await _drawerService.GetSettingAsync(BoxPositionSettingPrefix + boxId.ToString("N"));
        if (TryParseStoredPosition(savedPosition, out var left, out var top, out var isPhysicalPixels))
        {
            if (isPhysicalPixels)
            {
                // Create the HWND before restoring. Assigning physical coordinates
                // through WPF Left/Top would reinterpret them using the primary DPI.
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                window.MoveWindowOriginPixels(left, top);
            }
            else
            {
                // Old builds stored monitor-dependent WPF DIPs without a monitor id.
                // Interpret them once with the legacy behavior; the next save writes px:.
                window.Left = left;
                window.Top = top;
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            }

            var bounds = window.GetVisibleBoundsPixels();
            var workArea = window.GetWorkAreaPixels();
            if (bounds.IsEmpty || workArea.IsEmpty)
            {
                return false;
            }

            var origin = CalculateClampedVisibleOrigin(bounds, workArea);
            var wasClamped = Math.Abs(origin.X - bounds.Left) > 0.5
                || Math.Abs(origin.Y - bounds.Top) > 0.5;
            if (wasClamped)
            {
                window.MoveToVisibleOriginPixels(origin.X, origin.Y);
            }

            return wasClamped;
        }

        PlaceNewWindow(window, fallbackIndex);
        return true;
    }

    internal static string SerializePosition(double left, double top) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{left:R}{PositionSeparator}{top:R}");

    internal static string SerializePhysicalPosition(double leftPixels, double topPixels) =>
        PhysicalPositionPrefix + SerializePosition(leftPixels, topPixels);

    internal static bool TryParseStoredPosition(
        string? raw,
        out double left,
        out double top,
        out bool isPhysicalPixels)
    {
        isPhysicalPixels = raw?.StartsWith(PhysicalPositionPrefix, StringComparison.Ordinal) == true;
        var coordinates = isPhysicalPixels ? raw![PhysicalPositionPrefix.Length..] : raw;
        if (TryParsePosition(coordinates, out left, out top))
        {
            return true;
        }

        isPhysicalPixels = false;
        return false;
    }

    internal static bool TryParsePosition(string? raw, out double left, out double top)
    {
        left = 0;
        top = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(PositionSeparator, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top))
        {
            left = 0;
            top = 0;
            return false;
        }

        return double.IsFinite(left) && double.IsFinite(top);
    }

    private static string CaptureStoredPosition(DesktopBoxWindow window)
    {
        if (window.TryGetWindowBoundsPixels(out var bounds))
        {
            return SerializePhysicalPosition(bounds.Left, bounds.Top);
        }

        // A visible desktop box normally always has an HWND. Retain the legacy
        // fallback so shutdown cannot lose a position if a handle is being rebuilt.
        return SerializePosition(window.Left, window.Top);
    }

    private static LayoutBackupPosition CaptureLayoutBackupPosition(
        Guid boxId,
        DesktopBoxWindow window)
    {
        if (window.TryGetWindowBoundsPixels(out var bounds))
        {
            return new LayoutBackupPosition(boxId, bounds.Left, bounds.Top, IsPhysicalPixels: true);
        }

        return new LayoutBackupPosition(boxId, window.Left, window.Top);
    }

    internal static Point CalculateCenteredVisibleOrigin(Size visibleSize, Rect workArea)
    {
        if (!double.IsFinite(visibleSize.Width)
            || !double.IsFinite(visibleSize.Height)
            || visibleSize.Width < 0
            || visibleSize.Height < 0
            || workArea.IsEmpty
            || !double.IsFinite(workArea.Left)
            || !double.IsFinite(workArea.Top)
            || !double.IsFinite(workArea.Width)
            || !double.IsFinite(workArea.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(workArea),
                "Visible size and work area must contain finite, non-negative dimensions.");
        }

        var centeredLeft = workArea.Left + ((workArea.Width - visibleSize.Width) / 2);
        var centeredTop = workArea.Top + ((workArea.Height - visibleSize.Height) / 2);
        var left = Math.Max(
            workArea.Left,
            Math.Min(centeredLeft, workArea.Right - visibleSize.Width));
        var top = Math.Max(
            workArea.Top,
            Math.Min(centeredTop, workArea.Bottom - visibleSize.Height));
        return new Point(left, top);
    }

    internal static Point CalculateClampedVisibleOrigin(Rect visibleBounds, Rect workArea)
    {
        var left = Math.Max(
            workArea.Left,
            Math.Min(visibleBounds.Left, workArea.Right - visibleBounds.Width));
        var top = Math.Max(
            workArea.Top,
            Math.Min(visibleBounds.Top, workArea.Bottom - visibleBounds.Height));
        return new Point(left, top);
    }

    internal static string GetLayoutBackupSettingKey(int slot)
    {
        if (slot is < 1 or > LayoutBackupSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Layout backup slot must be from 1 to 3.");
        }

        return LayoutBackupSettingPrefix + slot.ToString(CultureInfo.InvariantCulture);
    }

    internal static string SerializeLayoutBackup(IEnumerable<LayoutBackupPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var snapshot = positions.ToArray();
        if (!IsValidLayoutBackup(snapshot))
        {
            throw new ArgumentException("Layout backup contains invalid or duplicate box positions.", nameof(positions));
        }

        return JsonSerializer.Serialize(new LayoutBackupPayload(LayoutBackupVersion, snapshot));
    }

    internal static bool TryParseLayoutBackup(string? raw, out LayoutBackupPosition[] positions)
    {
        positions = [];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<LayoutBackupPayload>(raw);
            if (payload is null
                || (payload.Version != LayoutBackupVersion
                    && payload.Version != LegacyLayoutBackupVersion)
                || !IsValidLayoutBackup(payload.Positions))
            {
                return false;
            }

            positions = payload.Positions;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValidLayoutBackup(LayoutBackupPosition[]? positions)
    {
        if (positions is null || positions.Length > MaxLayoutBackupPositions)
        {
            return false;
        }

        var ids = new HashSet<Guid>();
        return positions.All(position =>
            position.BoxId != Guid.Empty
            && double.IsFinite(position.Left)
            && double.IsFinite(position.Top)
            && ids.Add(position.BoxId));
    }

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

    private static void PlaceNewWindow(Window window, int index)
    {
        const double margin = 18;
        const double gap = 12;
        const double topPadding = 84;

        var workArea = SystemParameters.WorkArea;
        var centerX = workArea.Left + (workArea.Width - window.DesiredSize.Width) / 2;
        var centerY = workArea.Top + (workArea.Height - window.DesiredSize.Height) / 2;

        var offset = index * (window.DesiredSize.Width + gap);
        window.Left = Math.Max(workArea.Left + margin, Math.Min(centerX + offset, workArea.Right - window.DesiredSize.Width - margin));
        window.Top = Math.Max(workArea.Top + margin, Math.Min(centerY + topPadding * 0.5, workArea.Bottom - window.DesiredSize.Height - margin));
    }

    /// <summary>
    /// Nudges apart windows that were placed without a usable saved position this
    /// refresh (new boxes, or saved positions clamped by a resolution/monitor
    /// change). Windows restored from an intact saved position keep their exact
    /// spot — the user's arrangement is authoritative even when boxes overlap —
    /// but still count as obstacles so movable windows cascade around them.
    /// </summary>
    private void ResolveWindowOverlaps()
    {
        var entries = _windows.Values
            .Where(window => window.IsVisible)
            .OfType<DesktopBoxWindow>()
            .Select(window => (Window: window, Bounds: window.GetVisibleBoundsPixels()))
            .Where(entry => entry.Bounds.Width > 0 && entry.Bounds.Height > 0)
            .ToArray();

        // Restored positions are authoritative obstacles regardless of database order.
        var placed = entries
            .Where(entry => !_overlapResolutionBoxIds.Contains(entry.Window.ViewModel.BoxId))
            .Select(entry => entry.Bounds)
            .ToList();

        foreach (var entry in entries.Where(entry => _overlapResolutionBoxIds.Contains(entry.Window.ViewModel.BoxId)))
        {
            var bounds = entry.Bounds;
            var workArea = entry.Window.GetWorkAreaPixels();
            if (workArea.IsEmpty)
            {
                // A transient monitor query failure must not turn Rect.Empty's
                // infinities into an invalid native window position.
                placed.Add(bounds);
                continue;
            }

            var resolved = ResolveOverlapCascade(
                bounds,
                placed,
                workArea);

            if (Math.Abs(bounds.Left - resolved.Left) > 0.5
                || Math.Abs(bounds.Top - resolved.Top) > 0.5)
            {
                entry.Window.MoveToVisibleOriginPixels(resolved.Left, resolved.Top);
            }

            placed.Add(resolved);
        }
    }

    /// <summary>
    /// Cascades <paramref name="bounds"/> below/right of every rect in
    /// <paramref name="placed"/> until no overlap remains, then clamps the result
    /// into <paramref name="workArea"/>. Pure math kept separate from the window
    /// loop above so the collision rules stay unit-testable.
    /// </summary>
    internal static Rect ResolveOverlapCascade(
        Rect bounds,
        IReadOnlyList<Rect> placed,
        Rect workArea)
    {
        const double cascadeStep = 12;

        var moved = true;
        var guard = 0;
        while (moved && guard++ < 200)
        {
            moved = false;
            foreach (var other in placed)
            {
                if (!bounds.IntersectsWith(other))
                {
                    continue;
                }

                var nextLeft = bounds.Left;
                var nextTop = other.Bottom + cascadeStep;
                if (nextTop + bounds.Height > workArea.Bottom)
                {
                    // No room below; wrap to the right of the blocking box and
                    // restart from the top so the cascade stays on screen.
                    nextLeft = other.Right + cascadeStep;
                    nextTop = workArea.Top;
                }

                bounds = new Rect(nextLeft, nextTop, bounds.Width, bounds.Height);
                moved = true;
            }
        }

        // Clamp back into the work area so a wrapped cascade never leaves the box
        // hanging off the right/bottom edge.
        var clampedLeft = Math.Max(workArea.Left, Math.Min(bounds.Left, workArea.Right - bounds.Width));
        var clampedTop = Math.Max(workArea.Top, Math.Min(bounds.Top, workArea.Bottom - bounds.Height));
        if (Math.Abs(clampedLeft - bounds.Left) > 0.5
            || Math.Abs(clampedTop - bounds.Top) > 0.5)
        {
            bounds = new Rect(clampedLeft, clampedTop, bounds.Width, bounds.Height);
        }

        return bounds;
    }

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

    private void HideGuides()
    {
        HideVerticalGuide();
        HideHorizontalGuide();
    }

    private void ShowVerticalGuide(double x, double yStart, double height)
    {
        if (_verticalGuide == null)
        {
            _verticalGuide = new GuideLineWindow(true);
        }
        if (!_verticalGuide.IsVisible)
        {
            _verticalGuide.Show();
        }
        _verticalGuide.UpdateLine(x, yStart, x, yStart + height);
    }

    private void HideVerticalGuide()
    {
        _verticalGuide?.Hide();
    }

    private void ShowHorizontalGuide(double y, double xStart, double width)
    {
        if (_horizontalGuide == null)
        {
            _horizontalGuide = new GuideLineWindow(false);
        }
        if (!_horizontalGuide.IsVisible)
        {
            _horizontalGuide.Show();
        }
        _horizontalGuide.UpdateLine(xStart, y, xStart + width, y);
    }

    private void HideHorizontalGuide()
    {
        _horizontalGuide?.Hide();
    }

    private void PerformSnappingAndAlignment(DesktopBoxWindow draggedWindow, bool applySnap = true)
    {
        var dpi = draggedWindow.GetDpiScale();
        var snapThreshold = 10.0 * dpi.DpiScaleX;
        var visualGap = 8.0 * dpi.DpiScaleX;

        var boundsA = draggedWindow.GetVisibleBoundsPixels();
        if (boundsA.IsEmpty)
        {
            HideGuides();
            return;
        }
        double currentLeft = boundsA.Left;
        double currentTop = boundsA.Top;
        double width = boundsA.Width;
        double height = boundsA.Height;
        double rightA = boundsA.Right;
        double bottomA = boundsA.Bottom;
        double hCenterA = currentLeft + width / 2.0;
        double vCenterA = currentTop + height / 2.0;
        double? bestSnappedVisibleLeft = null;
        double? bestSnappedVisibleTop = null;

        double? verticalGuideX = null;
        double verticalGuideYMin = double.MaxValue;
        double verticalGuideYMax = double.MinValue;

        double? horizontalGuideY = null;
        double horizontalGuideXMin = double.MaxValue;
        double horizontalGuideXMax = double.MinValue;

        foreach (var pair in _windows)
        {
            var otherWindow = pair.Value;
            if (otherWindow == draggedWindow || !otherWindow.IsVisible)
            {
                continue;
            }

            var boundsB = otherWindow.GetVisibleBoundsPixels();
            if (boundsB.IsEmpty)
            {
                continue;
            }
            double leftB = boundsB.Left;
            double topB = boundsB.Top;
            double widthB = boundsB.Width;
            double heightB = boundsB.Height;
            double rightB = boundsB.Right;
            double bottomB = boundsB.Bottom;
            double hCenterB = leftB + widthB / 2.0;
            double vCenterB = topB + heightB / 2.0;

            // 1. Vertical snapping
            if (Math.Abs(currentLeft - leftB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB;
                verticalGuideX = leftB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - rightB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB - width;
                verticalGuideX = rightB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(currentLeft - (rightB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB + visualGap;
                verticalGuideX = rightB + visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - (leftB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB - visualGap - width;
                verticalGuideX = leftB - visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(hCenterA - hCenterB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = hCenterB - width / 2.0;
                verticalGuideX = hCenterB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }

            // 2. Horizontal snapping
            if (Math.Abs(currentTop - topB) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB;
                horizontalGuideY = topB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - bottomB) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB - height;
                horizontalGuideY = bottomB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(currentTop - (bottomB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB + visualGap;
                horizontalGuideY = bottomB + visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - (topB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB - visualGap - height;
                horizontalGuideY = topB - visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(vCenterA - vCenterB) <= snapThreshold)
            {
                bestSnappedVisibleTop = vCenterB - height / 2.0;
                horizontalGuideY = vCenterB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
        }

        if (applySnap)
        {
            if (bestSnappedVisibleLeft.HasValue || bestSnappedVisibleTop.HasValue)
            {
                draggedWindow.MoveToVisibleOriginPixels(
                    bestSnappedVisibleLeft ?? currentLeft,
                    bestSnappedVisibleTop ?? currentTop);
            }
        }

        if (verticalGuideX.HasValue && verticalGuideYMax > verticalGuideYMin)
        {
            ShowVerticalGuide(verticalGuideX.Value, verticalGuideYMin, verticalGuideYMax - verticalGuideYMin);
        }
        else
        {
            HideVerticalGuide();
        }

        if (horizontalGuideY.HasValue && horizontalGuideXMax > horizontalGuideXMin)
        {
            ShowHorizontalGuide(horizontalGuideY.Value, horizontalGuideXMin, horizontalGuideXMax - horizontalGuideXMin);
        }
        else
        {
            HideHorizontalGuide();
        }
    }

}
