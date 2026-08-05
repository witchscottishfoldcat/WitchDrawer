using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WitchDrawer.App.Controls;

internal interface IVirtualizingCanvasItem
{
    double VirtualizationLeft { get; }

    double VirtualizationTop { get; }
}

public sealed class VirtualizingCanvas : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingCanvas),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingCanvas),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(
        nameof(ContentWidth),
        typeof(double),
        typeof(VirtualizingCanvas),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ContentHeightProperty = DependencyProperty.Register(
        nameof(ContentHeight),
        typeof(double),
        typeof(VirtualizingCanvas),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OverscanItemCountProperty = DependencyProperty.Register(
        nameof(OverscanItemCount),
        typeof(int),
        typeof(VirtualizingCanvas),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double ContentWidth
    {
        get => (double)GetValue(ContentWidthProperty);
        set => SetValue(ContentWidthProperty, value);
    }

    public double ContentHeight
    {
        get => (double)GetValue(ContentHeightProperty);
        set => SetValue(ContentHeightProperty, value);
    }

    public int OverscanItemCount
    {
        get => (int)GetValue(OverscanItemCountProperty);
        set => SetValue(OverscanItemCountProperty, value);
    }

    public bool CanHorizontallyScroll { get; set; } = true;

    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    internal static IReadOnlyList<int> GetVisibleItemIndices(
        IList items,
        Rect viewport,
        Size itemSize,
        int overscanItemCount)
    {
        if (items.Count == 0 || viewport.IsEmpty)
        {
            return [];
        }

        var width = NormalizeDimension(itemSize.Width);
        var height = NormalizeDimension(itemSize.Height);
        var overscan = Math.Max(0, overscanItemCount);
        var overscannedViewport = viewport;
        overscannedViewport.Inflate(width * overscan, height * overscan);
        var indices = new List<int>();

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not IVirtualizingCanvasItem item)
            {
                continue;
            }

            var bounds = new Rect(
                item.VirtualizationLeft,
                item.VirtualizationTop,
                width,
                height);
            if (bounds.IntersectsWith(overscannedViewport))
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
        {
            return new Size();
        }

        var itemSize = new Size(
            NormalizeDimension(ItemWidth),
            NormalizeDimension(ItemHeight));
        var extent = new Size(
            Math.Max(itemSize.Width, NormalizeDimension(ContentWidth)),
            Math.Max(itemSize.Height, NormalizeDimension(ContentHeight)));
        var viewport = new Size(
            NormalizeViewportDimension(availableSize.Width, extent.Width),
            NormalizeViewportDimension(availableSize.Height, extent.Height));
        UpdateScrollInfo(extent, viewport);

        var visibleIndices = GetVisibleItemIndices(
            owner.Items,
            new Rect(_offset, _viewport),
            itemSize,
            OverscanItemCount);
        var visibleSet = visibleIndices.ToHashSet();
        RemoveInvisibleContainers(visibleSet);

        foreach (var itemIndex in visibleIndices)
        {
            var child = RealizeContainer(itemIndex);
            child?.Measure(itemSize);
        }

        return new Size(
            Math.Min(_extent.Width, _viewport.Width),
            Math.Min(_extent.Height, _viewport.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
        {
            return finalSize;
        }

        var generator = ItemContainerGenerator;
        var itemSize = new Size(
            NormalizeDimension(ItemWidth),
            NormalizeDimension(ItemHeight));
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0
                || itemIndex >= owner.Items.Count
                || owner.Items[itemIndex] is not IVirtualizingCanvasItem item)
            {
                continue;
            }

            InternalChildren[childIndex].Arrange(new Rect(
                item.VirtualizationLeft - _offset.X,
                item.VirtualizationTop - _offset.Y,
                itemSize.Width,
                itemSize.Height));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);

    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);

    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ItemWidth);

    public void LineRight() => SetHorizontalOffset(HorizontalOffset + ItemWidth);

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (ItemHeight * 3));

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (ItemHeight * 3));

    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - (ItemWidth * 3));

    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + (ItemWidth * 3));

    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    public void SetHorizontalOffset(double offset)
    {
        var normalized = CanHorizontallyScroll
            ? Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth))
            : 0;
        if (DoubleUtil.AreClose(normalized, _offset.X))
        {
            return;
        }

        _offset.X = normalized;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        var normalized = CanVerticallyScroll
            ? Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight))
            : 0;
        if (DoubleUtil.AreClose(normalized, _offset.Y))
        {
            return;
        }

        _offset.Y = normalized;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not FrameworkElement { DataContext: IVirtualizingCanvasItem item })
        {
            return rectangle;
        }

        var itemRect = new Rect(
            item.VirtualizationLeft,
            item.VirtualizationTop,
            NormalizeDimension(ItemWidth),
            NormalizeDimension(ItemHeight));
        if (itemRect.Left < HorizontalOffset)
        {
            SetHorizontalOffset(itemRect.Left);
        }
        else if (itemRect.Right > HorizontalOffset + ViewportWidth)
        {
            SetHorizontalOffset(itemRect.Right - ViewportWidth);
        }

        if (itemRect.Top < VerticalOffset)
        {
            SetVerticalOffset(itemRect.Top);
        }
        else if (itemRect.Bottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(itemRect.Bottom - ViewportHeight);
        }

        return new Rect(
            itemRect.Left - HorizontalOffset,
            itemRect.Top - VerticalOffset,
            itemRect.Width,
            itemRect.Height);
    }

    private UIElement? RealizeContainer(int itemIndex)
    {
        var generator = ItemContainerGenerator;
        var position = generator.GeneratorPositionFromIndex(itemIndex);
        var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            if (generator.GenerateNext(out var newlyRealized) is not UIElement child)
            {
                return null;
            }

            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                generator.PrepareItemContainer(child);
            }

            return child;
        }
    }

    private void RemoveInvisibleContainers(IReadOnlySet<int> visibleIndices)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (visibleIndices.Contains(itemIndex))
            {
                continue;
            }

            if (generator is IRecyclingItemContainerGenerator recyclingGenerator)
            {
                recyclingGenerator.Recycle(position, 1);
            }
            else
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = !DoubleUtil.AreClose(_extent.Width, extent.Width)
            || !DoubleUtil.AreClose(_extent.Height, extent.Height)
            || !DoubleUtil.AreClose(_viewport.Width, viewport.Width)
            || !DoubleUtil.AreClose(_viewport.Height, viewport.Height);
        _extent = extent;
        _viewport = viewport;
        _offset.X = Math.Clamp(_offset.X, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        _offset.Y = Math.Clamp(_offset.Y, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private static double NormalizeDimension(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }

    private static double NormalizeViewportDimension(double value, double extent)
    {
        return double.IsFinite(value) && value > 0 ? value : extent;
    }

    private static class DoubleUtil
    {
        public static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.1;
        }
    }
}
