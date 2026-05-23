using System.Windows;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed class DesktopBoxManager
{
    private const string BoxPositionSettingPrefix = "BoxPosition:";
    private const char PositionSeparator = ',';

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly DesktopBoxLayoutSettings _layoutSettings;
    private readonly Dictionary<Guid, DesktopBoxWindow> _windows = [];
    private bool _closing;
    private GuideLineWindow? _verticalGuide;
    private GuideLineWindow? _horizontalGuide;
    private bool _isAdjustingPosition;

    public DesktopBoxManager(
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger,
        DesktopBoxLayoutSettings layoutSettings)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
        _layoutSettings = layoutSettings;
    }

    public event EventHandler? ItemsChanged;

    public async Task RefreshAsync()
    {
        if (_closing)
        {
            return;
        }

        var boxes = await _drawerService.GetBoxesAsync();
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
            var box = boxes[index];
            if (!_windows.TryGetValue(box.Id, out var window))
            {
                var viewModel = new DesktopBoxViewModel(box, _drawerService, _launcher, _trash, _logger, _layoutSettings);
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
            }
            else
            {
                window.ViewModel.UpdateBox(box);
            }

            await window.ViewModel.LoadAsync();
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

        _verticalGuide?.Close();
        _verticalGuide = null;
        _horizontalGuide?.Close();
        _horizontalGuide = null;
    }

    private async Task PlaceWindowAsync(Window window, Guid boxId, int fallbackIndex)
    {
        // SizeToContent windows report NaN for Width/Height before they are shown; measure
        // first and use DesiredSize so saved positions are restored correctly.
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var savedPosition = await _drawerService.GetSettingAsync(BoxPositionSettingPrefix + boxId.ToString("N"));
        if (TryParsePosition(savedPosition, out var left, out var top))
        {
            var workArea = SystemParameters.WorkArea;
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

        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Right - window.DesiredSize.Width - margin;
        window.Top = workArea.Top + 84 + index * (window.DesiredSize.Height + gap);

        if (window.Top + window.DesiredSize.Height > workArea.Bottom - margin)
        {
            window.Top = workArea.Top + margin;
            window.Left = Math.Max(workArea.Left + margin, window.Left - window.DesiredSize.Width - gap);
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

        double currentLeft = draggedWindow.Left;
        double currentTop = draggedWindow.Top;
        double width = draggedWindow.ActualWidth;
        double height = draggedWindow.ActualHeight;

        double rightA = currentLeft + width;
        double bottomA = currentTop + height;
        double hCenterA = currentLeft + width / 2.0;
        double vCenterA = currentTop + height / 2.0;

        double? bestSnappedLeft = null;
        double? bestSnappedTop = null;

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

            double leftB = otherWindow.Left;
            double topB = otherWindow.Top;
            double widthB = otherWindow.ActualWidth;
            double heightB = otherWindow.ActualHeight;

            double rightB = leftB + widthB;
            double bottomB = topB + heightB;
            double hCenterB = leftB + widthB / 2.0;
            double vCenterB = topB + heightB / 2.0;

            // 1. Vertical snapping
            if (Math.Abs(currentLeft - leftB) <= snapThreshold)
            {
                bestSnappedLeft = leftB;
                verticalGuideX = leftB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - rightB) <= snapThreshold)
            {
                bestSnappedLeft = rightB - width;
                verticalGuideX = rightB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(currentLeft - rightB) <= snapThreshold)
            {
                bestSnappedLeft = rightB;
                verticalGuideX = rightB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - leftB) <= snapThreshold)
            {
                bestSnappedLeft = leftB - width;
                verticalGuideX = leftB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(hCenterA - hCenterB) <= snapThreshold)
            {
                bestSnappedLeft = hCenterB - width / 2.0;
                verticalGuideX = hCenterB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }

            // 2. Horizontal snapping
            if (Math.Abs(currentTop - topB) <= snapThreshold)
            {
                bestSnappedTop = topB;
                horizontalGuideY = topB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - bottomB) <= snapThreshold)
            {
                bestSnappedTop = bottomB - height;
                horizontalGuideY = bottomB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(currentTop - bottomB) <= snapThreshold)
            {
                bestSnappedTop = bottomB;
                horizontalGuideY = bottomB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - topB) <= snapThreshold)
            {
                bestSnappedTop = topB - height;
                horizontalGuideY = topB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(vCenterA - vCenterB) <= snapThreshold)
            {
                bestSnappedTop = vCenterB - height / 2.0;
                horizontalGuideY = vCenterB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
        }

        if (applySnap)
        {
            if (bestSnappedLeft.HasValue)
            {
                draggedWindow.Left = bestSnappedLeft.Value;
            }
            if (bestSnappedTop.HasValue)
            {
                draggedWindow.Top = bestSnappedTop.Value;
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
