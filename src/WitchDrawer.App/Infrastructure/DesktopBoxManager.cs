using System.Runtime.InteropServices;
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
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxItemNameVisibilityChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyItemNameVisibility(message));
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, DrawerSortModeChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyDrawerSortMode(message));
    }

    public event EventHandler? ItemsChanged;

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
                    await viewModel.LoadItemNameVisibilityAsync();
                    await viewModel.LoadMaxRowsAsync();
                    await viewModel.LoadDrawerSortModeAsync();
                    viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(this, EventArgs.Empty);

                    window = new DesktopBoxWindow(viewModel);
                    await PlaceWindowAsync(window, box.Id, index);
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

                    window.Show();
                    window.SetPositionLocked(isPositionLocked);
                    window.SetDesktopForeground(_desktopIsForeground);
                    window.QueueSendToBottom();
                }
                else
                {
                    window.ViewModel.UpdateBox(box, visualStyle);
                    window.SetPositionLocked(isPositionLocked);
                }

                await window.ViewModel.LoadAsync();
                window.SetDesktopForeground(_desktopIsForeground);
                window.QueueSendToBottom();
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
    public async Task RefreshItemsAsync()
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

            foreach (var window in _windows.Values.ToArray())
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

        if (ForegroundWindowMonitor.IsDesktopWindow(windowHandle))
        {
            SetDesktopForeground(true);
            return;
        }

        // Clicking a desktop box should not make it disappear while Show Desktop
        // is active. Any other foreground window ends the temporary topmost mode.
        if (_windows.Values.Any(window => window.NativeHandle == windowHandle))
        {
            return;
        }

        SetDesktopForeground(false);
    }

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

    private void ApplyItemNameVisibility(
        BoxItemNameVisibilityChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyItemNameVisibility(message.IsVisible);
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
        var workAreas = GetMonitorWorkAreasDip(window);
        var primaryWorkArea = workAreas.Count > 0 ? workAreas[0] : SystemParameters.WorkArea;

        double left;
        double top;
        if (TryParsePosition(savedPosition, out var savedLeft, out var savedTop))
        {
            (left, top) = ClampToContainingWorkArea(
                savedLeft,
                savedTop,
                window.DesiredSize,
                workAreas,
                primaryWorkArea);
        }
        else
        {
            (left, top) = PlaceNewWindowCore(window, fallbackIndex, primaryWorkArea);
        }

        // Anti-overlap: a saved position may collide with another box (e.g. after a
        // monitor was unplugged and several boxes were clamped onto the same screen).
        // Cascade the window to a nearby free spot instead of stacking it.
        (left, top) = FindNonOverlappingPosition(window, left, top, workAreas, primaryWorkArea);

        window.Left = left;
        window.Top = top;
    }

    private (double Left, double Top) FindNonOverlappingPosition(
        DesktopBoxWindow window,
        double left,
        double top,
        IReadOnlyList<Rect> workAreas,
        Rect primaryWorkArea)
    {
        const double gap = 12;
        const double margin = 8;
        var width = NormalizeDimension(window.DesiredSize.Width);
        var height = NormalizeDimension(window.DesiredSize.Height);

        var placedRects = _windows.Values
            .Where(candidate => candidate != window && candidate.IsVisible)
            .Select(candidate => new Rect(
                candidate.Left,
                candidate.Top,
                Math.Max(1, candidate.ActualWidth),
                Math.Max(1, candidate.ActualHeight)))
            .ToArray();
        if (placedRects.Length == 0)
        {
            return (left, top);
        }

        var workArea = FindContainingWorkArea(left, top, width, height, workAreas, primaryWorkArea);
        var rect = new Rect(left, top, width, height);
        if (placedRects.All(placed => !placed.IntersectsWith(rect)))
        {
            return (left, top);
        }

        var cascadeLeft = workArea.Left + margin;
        var cascadeTop = workArea.Top + margin;
        var currentLeft = cascadeLeft;
        var currentTop = cascadeTop;

        // Try the whole work area grid before giving up; afterwards accept the last
        // candidate so a fully saturated screen still places the window.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            rect = new Rect(currentLeft, currentTop, width, height);
            if (placedRects.All(placed => !placed.IntersectsWith(rect)))
            {
                return (currentLeft, currentTop);
            }

            currentLeft += width + gap;
            if (currentLeft + width > workArea.Right - margin)
            {
                currentLeft = cascadeLeft;
                currentTop += height + gap;
            }

            if (currentTop + height > workArea.Bottom - margin)
            {
                currentTop = cascadeTop;
            }
        }

        return (left, top);
    }

    private static (double Left, double Top) ClampToContainingWorkArea(
        double left,
        double top,
        Size windowSize,
        IReadOnlyList<Rect> workAreas,
        Rect primaryWorkArea)
    {
        var workArea = FindContainingWorkArea(left, top, windowSize.Width, windowSize.Height, workAreas, primaryWorkArea);
        return (
            Math.Max(workArea.Left, Math.Min(left, workArea.Right - windowSize.Width)),
            Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - windowSize.Height)));
    }

    private static double NormalizeDimension(double value)
    {
        return double.IsFinite(value) && value > 1 ? value : 1;
    }

    private static Rect FindContainingWorkArea(
        double left,
        double top,
        double width,
        double height,
        IReadOnlyList<Rect> workAreas,
        Rect primaryWorkArea)
    {
        if (workAreas.Count == 0)
        {
            return primaryWorkArea;
        }

        // Prefer the work area containing the window center; fall back to the
        // area with the largest intersection, then the primary screen.
        var centerX = left + width / 2;
        var centerY = top + height / 2;
        var centerContaining = workAreas.FirstOrDefault(area => area.Contains(centerX, centerY));
        if (centerContaining != default)
        {
            return centerContaining;
        }

        var windowRect = new Rect(left, top, Math.Max(1, width), Math.Max(1, height));
        var bestIntersection = double.MinValue;
        var bestArea = primaryWorkArea;
        foreach (var area in workAreas)
        {
            var intersection = Rect.Intersect(area, windowRect);
            var areaValue = intersection == Rect.Empty ? -1 : intersection.Width * intersection.Height;
            if (areaValue > bestIntersection)
            {
                bestIntersection = areaValue;
                bestArea = area;
            }
        }

        return bestArea;
    }

    /// <summary>
    /// Enumerates every monitor's work area and converts it to DIP using the
    /// window's DPI scale. Monitors are ordered primary-first.
    /// </summary>
    private static IReadOnlyList<Rect> GetMonitorWorkAreasDip(Window window)
    {
        var scale = 1.0;
        try
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            scale = Math.Max(0.1, dpi.DpiScaleX);
        }
        catch
        {
            // Fall back to unscaled physical pixels.
        }

        var monitors = new List<(NativeRect Work, bool IsPrimary)>();

        bool EnumMonitorCallback(
            nint monitorHandle,
            nint hdcMonitor,
            ref NativeRect monitorRect,
            nint data)
        {
            var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitorHandle, ref info))
            {
                monitors.Add((info.RcWork, (info.DwFlags & MonitorInfoPrimary) != 0));
            }

            return true;
        }

        EnumDisplayMonitors(nint.Zero, nint.Zero, EnumMonitorCallback, nint.Zero);

        var ordered = monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .Select(monitor => new Rect(
                monitor.Work.Left / scale,
                monitor.Work.Top / scale,
                (monitor.Work.Right - monitor.Work.Left) / scale,
                (monitor.Work.Bottom - monitor.Work.Top) / scale))
            .ToList();
        return ordered;
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
        var (left, top) = PlaceNewWindowCore(window, index, SystemParameters.WorkArea);
        window.Left = left;
        window.Top = top;
    }

    private static (double Left, double Top) PlaceNewWindowCore(
        Window window,
        int index,
        Rect workArea)
    {
        const double margin = 18;
        const double gap = 12;
        const double topPadding = 84;

        var centerX = workArea.Left + (workArea.Width - window.DesiredSize.Width) / 2;
        var centerY = workArea.Top + (workArea.Height - window.DesiredSize.Height) / 2;

        var offset = index * (window.DesiredSize.Width + gap);
        var left = Math.Max(workArea.Left + margin, Math.Min(centerX + offset, workArea.Right - window.DesiredSize.Width - margin));
        var top = Math.Max(workArea.Top + margin, Math.Min(centerY + topPadding * 0.5, workArea.Bottom - window.DesiredSize.Height - margin));
        return (left, top);
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

        var boundsA = GetVisibleBounds(draggedWindow);
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

            var boundsB = GetVisibleBounds(otherWindow);
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

    private static Rect GetVisibleBounds(DesktopBoxWindow window)
    {
        var margin = window.WindowBorder.Margin;
        return new Rect(
            window.Left + margin.Left,
            window.Top + margin.Top,
            Math.Max(0, window.ActualWidth - margin.Left - margin.Right),
            Math.Max(0, window.ActualHeight - margin.Top - margin.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int CbSize;
        public NativeRect RcMonitor;
        public NativeRect RcWork;
        public uint DwFlags;
    }

    private const uint MonitorInfoPrimary = 1;

    private delegate bool MonitorEnumProc(
        nint monitorHandle,
        nint hdcMonitor,
        ref NativeRect monitorRect,
        nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfo info);
}
