using System.Windows;
using System.Windows.Controls;

namespace WitchDrawer.App.Controls;

public sealed class CenteredUniformPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(int),
        typeof(CenteredUniformPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, Columns);
        var constrainedWidth = double.IsFinite(availableSize.Width) && availableSize.Width > 0;
        var cellSize = constrainedWidth ? availableSize.Width / columns : 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(constrainedWidth
                ? new Size(cellSize, cellSize)
                : new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (!constrainedWidth)
            {
                cellSize = Math.Max(cellSize, Math.Max(child.DesiredSize.Width, child.DesiredSize.Height));
            }
        }

        cellSize = Math.Max(1, cellSize);
        var rows = Math.Max(1, (int)Math.Ceiling(InternalChildren.Count / (double)columns));
        return new Size(columns * cellSize, rows * cellSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        var cellSize = finalSize.Width / columns;
        var rows = Math.Max(1, (int)Math.Ceiling(InternalChildren.Count / (double)columns));

        for (var row = 0; row < rows; row++)
        {
            var firstIndex = row * columns;
            var countInRow = Math.Min(columns, InternalChildren.Count - firstIndex);
            var horizontalOffset = (columns - countInRow) * cellSize / 2;
            for (var column = 0; column < countInRow; column++)
            {
                InternalChildren[firstIndex + column].Arrange(new Rect(
                    horizontalOffset + (column * cellSize),
                    row * cellSize,
                    cellSize,
                    cellSize));
            }
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, rows * cellSize));
    }
}
