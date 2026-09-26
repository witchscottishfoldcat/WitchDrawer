using System.Windows;
using System.Windows.Controls;

namespace WitchDrawer.App.Controls;

/// <summary>
/// Arranges drawer-cover tiles at fixed cell coordinates. UniformGrid divides its
/// rounded final width by the column count, which shifts existing icons by a
/// fraction of a DIP whenever the drawer gains or loses a column.
/// </summary>
public sealed class FixedCellGridPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(FixedCellGridPanel),
        new FrameworkPropertyMetadata(2,
            FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(FixedCellGridPanel),
        new FrameworkPropertyMetadata(1,
            FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(FixedCellGridPanel),
        new FrameworkPropertyMetadata(51d,
            FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(FixedCellGridPanel),
        new FrameworkPropertyMetadata(44d,
            FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsArrange));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = GetCellSize();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(cell);
        }

        return new Size(Math.Max(1, Columns) * cell.Width, Math.Max(1, Rows) * cell.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        var cell = GetCellSize();
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            InternalChildren[index].Arrange(new Rect(
                (index % columns) * cell.Width,
                (index / columns) * cell.Height,
                cell.Width,
                cell.Height));
        }

        return finalSize;
    }

    private Size GetCellSize() => new(
        double.IsFinite(CellWidth) && CellWidth > 0 ? CellWidth : 1,
        double.IsFinite(CellHeight) && CellHeight > 0 ? CellHeight : 1);
}
