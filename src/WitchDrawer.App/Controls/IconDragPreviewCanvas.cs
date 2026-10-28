using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WitchDrawer.App.Controls;

/// <summary>Anchors the preview to the real items host, including theme and DPI insets.</summary>
public sealed class IconDragPreviewCanvas : Canvas
{
    public static readonly DependencyProperty SourceListProperty = DependencyProperty.Register(
        nameof(SourceList), typeof(ListBox), typeof(IconDragPreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsArrange,
            (owner, _) => ((IconDragPreviewCanvas)owner)._itemsHost = null));

    public static readonly DependencyProperty SourceCoverProperty = DependencyProperty.Register(
        nameof(SourceCover), typeof(ItemsControl), typeof(IconDragPreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsArrange,
            (owner, _) => ((IconDragPreviewCanvas)owner)._coverHost = null));

    private VirtualizingCanvas? _itemsHost;
    private FixedCellGridPanel? _coverHost;
    private readonly TranslateTransform _origin = new();
    private bool _observingLayout;
    private bool _wasLoaded;

    public IconDragPreviewCanvas()
    {
        // ArrangeOverride runs before this element receives its final layout
        // offset. Resolve sibling coordinates after layout, especially when the
        // overlay becomes visible for the first time.
        Loaded += (_, _) =>
        {
            _wasLoaded = true;
            UpdateLayoutObservation();
        };
        Unloaded += (_, _) =>
        {
            ObserveLayout(false);
            _itemsHost = null;
            _coverHost = null;
        };
        IsVisibleChanged += (_, _) => UpdateLayoutObservation();
    }

    public ListBox? SourceList
    {
        get => (ListBox?)GetValue(SourceListProperty);
        set => SetValue(SourceListProperty, value);
    }

    public ItemsControl? SourceCover
    {
        get => (ItemsControl?)GetValue(SourceCoverProperty);
        set => SetValue(SourceCoverProperty, value);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateLayoutObservation();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty) UpdateLayoutObservation();
    }

    private void UpdateLayoutObservation() => ObserveLayout(
        Visibility == Visibility.Visible
        && VisualTreeHelper.GetParent(this) is not null
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
        if (Visibility != Visibility.Visible) return;
        var origin = new Point();
        if (SourceList is { } list)
        {
            if (_itemsHost is not null && !list.IsAncestorOf(_itemsHost)) _itemsHost = null;
            _itemsHost ??= FindDescendant<VirtualizingCanvas>(list);
            if (_itemsHost is { } host && host.FindCommonVisualAncestor(this) is not null)
            {
                origin = host.TranslatePoint(new Point(-host.HorizontalOffset, -host.VerticalOffset), this);
            }
        }
        else if (SourceCover is { } cover)
        {
            if (_coverHost is not null && !cover.IsAncestorOf(_coverHost)) _coverHost = null;
            _coverHost ??= FindDescendant<FixedCellGridPanel>(cover);
            if (_coverHost is { } host && host.FindCommonVisualAncestor(this) is not null)
            {
                // Resolve after arrange: a collapsed overlay has no useful offset
                // when the first DragOver requests the preview.
                origin = host.TranslatePoint(new Point(), this);
            }
        }

        if (_origin.X != origin.X) _origin.X = origin.X;
        if (_origin.Y != origin.Y) _origin.Y = origin.Y;
        foreach (UIElement child in InternalChildren)
        {
            if (!ReferenceEquals(child.RenderTransform, _origin)) child.RenderTransform = _origin;
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }

        return null;
    }
}
