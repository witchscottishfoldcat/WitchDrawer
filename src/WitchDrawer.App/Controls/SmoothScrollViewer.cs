using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WitchDrawer.App.Controls;

public sealed class SmoothScrollViewer : ScrollViewer
{
    private const double PixelsPerWheelLine = 12;
    private const double WheelDeltaPerNotch = 120;
    private const double MinimumDistance = 0.1;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    private bool _isAnimating;
    private long _startedAt;
    private double _startOffset;
    private double _targetOffset;

    public SmoothScrollViewer()
    {
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewScrollCommand);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopScrolling();
        };
        Unloaded += (_, _) => StopScrolling();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || e.Delta == 0 || ScrollableHeight <= 0) return;

        var lines = SystemParameters.WheelScrollLines;
        var distancePerNotch = lines < 0 ? ViewportHeight : lines * PixelsPerWheelLine;
        if (distancePerNotch > 0)
            ScrollBy(-e.Delta / WheelDeltaPerNotch * distancePerNotch);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        StopScrolling();
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        StopScrolling();
        base.OnPreviewKeyDown(e);
    }

    private void OnPreviewScrollCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == ScrollBar.PageUpCommand)
            ScrollBy(-ViewportHeight);
        else if (e.Command == ScrollBar.PageDownCommand)
            ScrollBy(ViewportHeight);
        else
            return;
        e.Handled = true;
    }

    private void ScrollBy(double distance)
    {
        var current = VerticalOffset;
        var remaining = _targetOffset - current;
        var start = _isAnimating && Math.Sign(remaining) == Math.Sign(distance)
            ? _targetOffset : current;
        var target = Math.Clamp(start + distance, 0, ScrollableHeight);
        if (_isAnimating && Math.Abs(target - _targetOffset) < MinimumDistance) return;

        if (!SystemParameters.ClientAreaAnimation || Math.Abs(target - current) < MinimumDistance)
        {
            StopScrolling();
            ScrollToVerticalOffset(target);
            return;
        }

        _startOffset = current;
        _targetOffset = target;
        _startedAt = Stopwatch.GetTimestamp();
        if (_isAnimating) return;
        _isAnimating = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var target = Math.Clamp(_targetOffset, 0, ScrollableHeight);
        var progress = Math.Min(1, Stopwatch.GetElapsedTime(_startedAt) / Duration);
        var eased = 1 - (1 - progress) * (1 - progress);
        ScrollToVerticalOffset(_startOffset + (target - _startOffset) * eased);
        if (progress >= 1) StopScrolling();
    }

    private void StopScrolling()
    {
        if (!_isAnimating) return;
        CompositionTarget.Rendering -= OnRendering;
        _isAnimating = false;
    }
}
