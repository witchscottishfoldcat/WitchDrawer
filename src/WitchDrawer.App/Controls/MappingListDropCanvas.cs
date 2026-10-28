using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WitchDrawer.App.Controls;

/// <summary>Positions the insertion line after the overlay and list have been arranged.</summary>
public sealed class MappingListDropCanvas : Canvas
{
    public static readonly DependencyProperty SourceListProperty = DependencyProperty.Register(
        nameof(SourceList), typeof(ListBox), typeof(MappingListDropCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsArrange));

    private readonly TranslateTransform _position = new();
    private FrameworkElement? _boundary;
    private bool _afterBoundary;
    private bool _observingLayout;
    private bool _wasLoaded;

    public MappingListDropCanvas()
    {
        Loaded += (_, _) => { _wasLoaded = true; UpdateLayoutObservation(); };
        Unloaded += (_, _) => { ObserveLayout(false); _boundary = null; };
        IsVisibleChanged += (_, _) => UpdateLayoutObservation();
    }

    public ListBox? SourceList
    {
        get => (ListBox?)GetValue(SourceListProperty);
        set => SetValue(SourceListProperty, value);
    }

    internal void ShowBoundary(FrameworkElement boundary, bool afterBoundary)
    {
        if (ReferenceEquals(_boundary, boundary) && _afterBoundary == afterBoundary
            && Visibility == Visibility.Visible) return;
        _boundary = boundary;
        _afterBoundary = afterBoundary;
        Visibility = Visibility.Visible;
        InvalidateArrange();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateLayoutObservation();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != VisibilityProperty) return;
        if (Visibility != Visibility.Visible) _boundary = null;
        UpdateLayoutObservation();
    }

    private void UpdateLayoutObservation() => ObserveLayout(
        Visibility == Visibility.Visible && VisualTreeHelper.GetParent(this) is not null
        && (IsVisible || !_wasLoaded));

    private void ObserveLayout(bool observe)
    {
        if (observe == _observingLayout) return;
        _observingLayout = observe;
        if (observe) LayoutUpdated += OnLayoutUpdated;
        else LayoutUpdated -= OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (SourceList is not { } list || _boundary is not { } boundary
            || InternalChildren.Count == 0 || InternalChildren[0] is not FrameworkElement indicator
            || boundary.FindCommonVisualAncestor(this) is null) return;

        var point = boundary.TranslatePoint(new Point(0,
            ReferenceEquals(boundary, list) ? list.Padding.Top : _afterBoundary ? boundary.ActualHeight : 0), this);
        var listOrigin = list.TranslatePoint(new Point(), this);
        var inset = Math.Max(6, list.Padding.Left);
        var left = listOrigin.X + inset;
        var top = point.Y - 1;
        if (_position.X != left) _position.X = left;
        if (_position.Y != top) _position.Y = top;
        if (!ReferenceEquals(indicator.RenderTransform, _position)) indicator.RenderTransform = _position;
        var width = Math.Max(indicator.MinWidth,
            list.ActualWidth - inset - Math.Max(6, list.Padding.Right));
        if (indicator.Width != width) indicator.Width = width;
    }
}
