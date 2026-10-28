using System.Windows;
using System.Windows.Media;

namespace WitchDrawer.Performance;

/// <summary>One drawing, bounded history, no per-point controls or frame animation.</summary>
internal sealed class TrendGraph : FrameworkElement
{
    private const int Capacity = 31;
    private readonly (DateTimeOffset Time, double? Value)[] _samples = new (DateTimeOffset, double?)[Capacity];
    private int _count;
    private int _next;

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TrendGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(TrendGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public TrendGraph()
    {
        Height = 40;
        SetResourceReference(StrokeProperty, "AccentBrush");
        SetResourceReference(GridBrushProperty, "BorderBrushSoft");
        IsHitTestVisible = false;
    }

    public void Add(double? value, DateTimeOffset capturedAt)
    {
        _samples[_next] = (capturedAt, value is { } number && double.IsFinite(number) ? Math.Clamp(number, 0, 100) : null);
        _next = (_next + 1) % Capacity;
        _count = Math.Min(Capacity, _count + 1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width = ActualWidth;
        var height = ActualHeight - 4;
        if (width <= 0 || height <= 0) return;
        var grid = new Pen(GridBrush, 1);
        drawing.DrawLine(grid, new Point(0, 2 + height), new Point(width, 2 + height));
        drawing.PushOpacity(0.4);
        drawing.DrawLine(grid, new Point(0, 2 + height / 2), new Point(width, 2 + height / 2));
        drawing.Pop();
        if (_count == 0) return;

        var geometry = new StreamGeometry();
        var open = false;
        Point? last = null;
        var now = DateTimeOffset.UtcNow;
        using (var path = geometry.Open())
        {
            for (var index = 0; index < _count; index++)
            {
                var sample = _samples[(_next - _count + index + Capacity) % Capacity];
                var age = (now - sample.Time).TotalSeconds;
                if (sample.Value is not { } value || age > 60) { open = false; last = null; continue; }
                var point = new Point(width * (1 - Math.Clamp(age / 60, 0, 1)), 2 + height * (1 - value / 100));
                if (!open) { path.BeginFigure(point, false, false); open = true; }
                else path.LineTo(point, true, false);
                last = point;
            }
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, new Pen(Stroke, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        if (last is { } end) drawing.DrawEllipse(Stroke, null, end, 2.5, 2.5);
    }
}
