using System.Threading;
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
    private const char PositionSeparator = ',';

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private readonly Dictionary<Guid, DesktopBoxWindow> _windows = [];
    private readonly ForegroundWindowMonitor _foregroundWindowMonitor;
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
        BoxPositionLockStateStore boxPositionLockStateStore)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        _foregroundWindowMonitor = new ForegroundWindowMonitor();
        _foregroundWindowMonitor.ForegroundWindowChanged += OnForegroundWindowChanged;
        _desktopIsForeground = ForegroundWindowMonitor.IsDesktopWindow(
            ForegroundWindowMonitor.GetCurrentForegroundWindow());
        if (!_foregroundWindowMonitor.IsActive)
        {
            _logger.Info("Foreground window monitoring is unavailable; Show Desktop layering may be limited.");
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
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, DrawerSortModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyDrawerSortMode(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxSizeModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxSizeMode(message));
    }

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;

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

            foreach (var removedId in _windows.Keys.Where(id => !boxIds.Contains(id)).ToArray())
            {
                var win = _windows[removedId];
                win.LocationChanged -= OnWindowLocationChanged;
                win.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
                win.Activated -= OnDesktopBoxActivated;
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
                    await viewModel.LoadDrawerCoverSizeAsync();
                    await viewModel.LoadTitleVisibilityAsync();
                    await viewModel.LoadDrawerSortModeAsync();
                    await viewModel.LoadSizeModeAsync();
                    viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(
                        this,
                        new BoxItemsChangedEventArgs(viewModel.BoxId));

                    window = new DesktopBoxWindow(viewModel);
                    await PlaceWindowAsync(window, box.Id, index);
                    _windows.Add(box.Id, window);

                    window.LocationChanged += OnWindowLocationChanged;
                    window.PreviewMouseLeftButtonUp += OnWindowMouseUp;
                    window.Activated += OnDesktopBoxActivated;
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
        foreach (var (boxId, window) in _windows)
        {
            var key = BoxPositionSettingPrefix + boxId.ToString("N");
            var value = $"{window.Left}{PositionSeparator}{window.Top}";
            await _drawerService.SetSettingAsync(key, value);
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
            window.Activated -= OnDesktopBoxActivated;
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

        if (_windows.TryGetValue(boxId, out var window) && !window.IsVisible)
        {
            await window.ViewModel.LoadAsync();
            window.Show();
            window.QueueSendToBottom();
            return true;
        }

        // Window was destroyed (e.g. fully closed) or never created this session:
        // refresh so the box window is recreated for the current box set.
        await RefreshAsync();
        return _windows.TryGetValue(boxId, out var refreshed) && refreshed.IsVisible;
    }

    public async Task SavePositionAsync(Guid boxId)
    {
        if (!_windows.TryGetValue(boxId, out var window))
        {
            return;
        }

        var key = BoxPositionSettingPrefix + boxId.ToString("N");
        var value = $"{window.Left}{PositionSeparator}{window.Top}";
        await _drawerService.SetSettingAsync(key, value);
    }

    public async Task CloseAllAsync()
    {
        _closing = true;
        await SaveAllPositionsAsync();
        foreach (var window in _windows.Values)
        {
            window.LocationChanged -= OnWindowLocationChanged;
            window.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
            window.Activated -= OnDesktopBoxActivated;
            window.ForceClose();
        }

        _windows.Clear();
        var foregroundChangeCts = Interlocked.Exchange(ref _foregroundChangeCts, null);
        foregroundChangeCts?.Cancel();
        foregroundChangeCts?.Dispose();
        _foregroundWindowMonitor.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _foregroundWindowMonitor.Dispose();

        _verticalGuide?.Close();
        _verticalGuide = null;
        _horizontalGuide?.Close();
        _horizontalGuide = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
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
        _ = ApplyForegroundWindowAfterSettlingAsync(next);
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

    private void OnDesktopBoxActivated(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        // A box activation ends Show Desktop mode immediately. Waiting for the
        // coalesced foreground hook leaves every Shell-owned box raised together
        // for several frames.
        SetDesktopForeground(ResolveDesktopForegroundState(
            isDesktopWindow: false,
            isDesktopBoxWindow: true));
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

    private void ApplyDrawerSortMode(DrawerSortModeChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyDrawerSortMode(message.SortMode);
        }
    }

    private async Task PlaceWindowAsync(DesktopBoxWindow window, Guid boxId, int fallbackIndex)
    {
        // SizeToContent windows report NaN for Width/Height before they are shown; measure
        // first and use DesiredSize so saved positions are restored correctly.
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var savedPosition = await _drawerService.GetSettingAsync(BoxPositionSettingPrefix + boxId.ToString("N"));
        if (TryParsePosition(savedPosition, out var left, out var top))
        {
            // 先按存档位置落位并创建句柄，确保取到目标位置所在显示器的工作区再钳制；
            // SystemParameters.WorkArea 只有主屏，会把副屏上的存档位置错钳回主屏。
            window.Left = left;
            window.Top = top;
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var workArea = window.GetWorkAreaDip();
            window.Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - window.DesiredSize.Width));
            window.Top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - window.DesiredSize.Height));
            return;
        }

        PlaceNewWindow(window, fallbackIndex);
    }

    private static bool TryParsePosition(string? raw, out double left, out double top)
    {
        left = 0;
        top = 0;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var parts = raw.Split(PositionSeparator);
        if (parts.Length != 2)
        {
            return false;
        }

        return double.TryParse(parts[0], out left) && double.TryParse(parts[1], out top);
    }

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
    /// Nudges windows apart so no two boxes overlap. Restored positions can collide
    /// (e.g. after a resolution/monitor change clamps several boxes to the same
    /// edge), so each window is cascaded below/right of whatever it overlaps.
    /// </summary>
    private void ResolveWindowOverlaps()
    {
        const double cascadeStep = 12;

        var workArea = SystemParameters.WorkArea;
        var placed = new List<Rect>();
        foreach (var window in _windows.Values.Where(w => w.IsVisible))
        {
            if (window is not DesktopBoxWindow boxWindow)
            {
                continue;
            }

            var bounds = boxWindow.GetVisibleBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

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

            // 比较与写回都必须在可视区域坐标系中进行：bounds 是可视区域矩形，
            // 而窗口 Left/Top 包含阴影留白 Margin，混用会让窗口每次消解都平移一圈。
            var currentBounds = boxWindow.GetVisibleBounds();
            if (Math.Abs(currentBounds.Left - bounds.Left) > 0.5
                || Math.Abs(currentBounds.Top - bounds.Top) > 0.5)
            {
                boxWindow.MoveToVisibleOrigin(bounds.Left, bounds.Top);
            }

            placed.Add(bounds);
        }
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
            _ = SavePositionAsync(window.ViewModel.BoxId);
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
        _verticalGuide.UpdateLine(x, yStart, x, yStart + height);
        if (!_verticalGuide.IsVisible)
        {
            _verticalGuide.Show();
        }
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
        _horizontalGuide.UpdateLine(xStart, y, xStart + width, y);
        if (!_horizontalGuide.IsVisible)
        {
            _horizontalGuide.Show();
        }
    }

    private void HideHorizontalGuide()
    {
        _horizontalGuide?.Hide();
    }

    private void PerformSnappingAndAlignment(DesktopBoxWindow draggedWindow, bool applySnap = true)
    {
        const double snapThreshold = 10.0;
        const double visualGap = 8.0;

        var boundsA = draggedWindow.GetVisibleBounds();
        double currentLeft = boundsA.Left;
        double currentTop = boundsA.Top;
        double width = boundsA.Width;
        double height = boundsA.Height;
        double rightA = boundsA.Right;
        double bottomA = boundsA.Bottom;
        double hCenterA = currentLeft + width / 2.0;
        double vCenterA = currentTop + height / 2.0;
        double leftInset = boundsA.Left - draggedWindow.Left;
        double topInset = boundsA.Top - draggedWindow.Top;

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

            var boundsB = otherWindow.GetVisibleBounds();
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
            if (bestSnappedVisibleLeft.HasValue)
            {
                draggedWindow.Left = bestSnappedVisibleLeft.Value - leftInset;
            }
            if (bestSnappedVisibleTop.HasValue)
            {
                draggedWindow.Top = bestSnappedVisibleTop.Value - topInset;
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
