using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WitchDrawer.App.Controls;

public sealed class CenteredUniformPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(int),
        typeof(CenteredUniformPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(
        nameof(CellSize),
        typeof(double),
        typeof(CenteredUniformPanel),
        new FrameworkPropertyMetadata(44d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent;
    private Size _viewport;
    private double _verticalOffset;

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _verticalOffset;

    public ScrollViewer? ScrollOwner { get; set; }

    internal static VisibleItemRange CalculateVisibleItemRange(
        int itemCount,
        int columns,
        double cellSize,
        double verticalOffset,
        double viewportHeight,
        int overscanRows)
    {
        if (itemCount <= 0)
        {
            return VisibleItemRange.Empty;
        }

        columns = Math.Max(1, columns);
        cellSize = NormalizeDimension(cellSize);
        viewportHeight = NormalizeDimension(viewportHeight);
        var totalRows = (int)Math.Ceiling(itemCount / (double)columns);
        var extentHeight = totalRows * cellSize;
        var normalizedOffset = Math.Clamp(
            verticalOffset,
            0,
            Math.Max(0, extentHeight - viewportHeight));
        var firstVisibleRow = (int)Math.Floor(normalizedOffset / cellSize);
        var lastVisibleRow = (int)Math.Ceiling(
            (normalizedOffset + viewportHeight) / cellSize) - 1;
        var overscan = Math.Max(0, overscanRows);
        var firstRow = Math.Max(0, firstVisibleRow - overscan);
        var lastRow = Math.Min(totalRows - 1, lastVisibleRow + overscan);

        return new VisibleItemRange(
            firstRow * columns,
            Math.Min(itemCount - 1, ((lastRow + 1) * columns) - 1));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
        {
            return new Size();
        }

        var columns = Math.Max(1, Columns);
        var fallbackCellSize = NormalizeDimension(CellSize);
        var viewportWidth = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : columns * fallbackCellSize;
        var cellSize = CalculateCellSize(fallbackCellSize, viewportWidth, columns);
        var rows = Math.Max(1, (int)Math.Ceiling(owner.Items.Count / (double)columns));
        var extent = new Size(viewportWidth, rows * cellSize);
        var viewportHeight = double.IsFinite(availableSize.Height) && availableSize.Height > 0
            ? availableSize.Height
            : extent.Height;
        UpdateScrollInfo(extent, new Size(viewportWidth, viewportHeight));

        var range = CalculateVisibleItemRange(
            owner.Items.Count,
            columns,
            cellSize,
            VerticalOffset,
            ViewportHeight,
            overscanRows: 1);
        RemoveContainersOutside(range);
        RealizeRange(range, new Size(cellSize, cellSize));

        return new Size(
            Math.Min(ExtentWidth, ViewportWidth),
            Math.Min(ExtentHeight, ViewportHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
        {
            return finalSize;
        }

        var columns = Math.Max(1, Columns);
        var cellSize = CalculateCellSize(CellSize, finalSize.Width, columns);
        var gridWidth = columns * cellSize;
        var gridLeft = Math.Max(0, (finalSize.Width - gridWidth) / 2);
        var itemCount = owner.Items.Count;
        var generator = ItemContainerGenerator;
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0 || itemIndex >= itemCount)
            {
                continue;
            }

            var row = itemIndex / columns;
            var column = itemIndex % columns;
            InternalChildren[childIndex].Arrange(new Rect(
                gridLeft + (column * cellSize),
                (row * cellSize) - VerticalOffset,
                cellSize,
                cellSize));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - NormalizeDimension(CellSize));

    public void LineDown() => SetVerticalOffset(VerticalOffset + NormalizeDimension(CellSize));

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (NormalizeDimension(CellSize) * 3));

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (NormalizeDimension(CellSize) * 3));

    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void SetVerticalOffset(double offset)
    {
        var normalized = CanVerticallyScroll
            ? Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight))
            : 0;
        if (Math.Abs(normalized - _verticalOffset) < 0.1)
        {
            return;
        }

        _verticalOffset = normalized;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not FrameworkElement element)
        {
            return rectangle;
        }

        var owner = ItemsControl.GetItemsOwner(this);
        var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(element) ?? -1;
        if (itemIndex < 0)
        {
            return rectangle;
        }

        var cellSize = Math.Max(1, ViewportWidth / Math.Max(1, Columns));
        var top = (itemIndex / Math.Max(1, Columns)) * cellSize;
        if (top < VerticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (top + cellSize > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(top + cellSize - ViewportHeight);
        }

        return new Rect(0, top - VerticalOffset, cellSize, cellSize);
    }

    private void RealizeRange(VisibleItemRange range, Size cellSize)
    {
        if (range.IsEmpty)
        {
            return;
        }

        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(range.FirstIndex);
        var childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;
        using (generator.StartAt(
                   startPosition,
                   GeneratorDirection.Forward,
                   allowStartAtRealizedItem: true))
        {
            for (var itemIndex = range.FirstIndex;
                 itemIndex <= range.LastIndex;
                 itemIndex++, childIndex++)
            {
                if (generator.GenerateNext(out var newlyRealized) is not UIElement child)
                {
                    continue;
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

                child.Measure(cellSize);
            }
        }
    }

    private void RemoveContainersOutside(VisibleItemRange range)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (!range.IsEmpty
                && itemIndex >= range.FirstIndex
                && itemIndex <= range.LastIndex)
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
        var changed = Math.Abs(_extent.Width - extent.Width) >= 0.1
            || Math.Abs(_extent.Height - extent.Height) >= 0.1
            || Math.Abs(_viewport.Width - viewport.Width) >= 0.1
            || Math.Abs(_viewport.Height - viewport.Height) >= 0.1;
        _extent = extent;
        _viewport = viewport;
        _verticalOffset = Math.Clamp(
            _verticalOffset,
            0,
            Math.Max(0, ExtentHeight - ViewportHeight));
        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private static double NormalizeDimension(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }

    private static double CalculateCellSize(
        double requestedCellSize,
        double viewportWidth,
        int columns)
    {
        var availableCellWidth = NormalizeDimension(viewportWidth) / Math.Max(1, columns);
        return Math.Max(1, Math.Min(NormalizeDimension(requestedCellSize), availableCellWidth));
    }

    internal readonly record struct VisibleItemRange(int FirstIndex, int LastIndex)
    {
        public static VisibleItemRange Empty { get; } = new(-1, -1);

        public bool IsEmpty => FirstIndex < 0 || LastIndex < FirstIndex;
    }
}
