using System.Collections.Specialized;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WitchDrawer.App.Controls;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class PerformanceVirtualizationTests
{
    [Fact]
    public void VirtualizingCanvas_SelectsOnlyItemsIntersectingTheOverscannedViewport()
    {
        var settings = new DesktopBoxLayoutSettings();
        var visible = new TestCanvasItem(0, 0);
        var nearby = new TestCanvasItem(settings.ItemSlotWidth, 0);
        var distant = new TestCanvasItem(
            settings.ItemSlotWidth * 12,
            settings.ItemSlotHeight * 8);

        var indices = VirtualizingCanvas.GetVisibleItemIndices(
            new object[] { distant, visible, nearby },
            new Rect(0, 0, settings.ItemSlotWidth, settings.ItemSlotHeight),
            new Size(settings.ItemSlotWidth, settings.ItemSlotHeight),
            overscanItemCount: 1);

        Assert.Equal([1, 2], indices);
    }

    [Fact]
    public void VirtualizingCanvas_RealizesOnlyViewportContainers()
    {
        Exception? threadException = null;
        var realizedCount = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var panelFactory = new FrameworkElementFactory(typeof(VirtualizingCanvas));
                panelFactory.SetValue(VirtualizingCanvas.ItemWidthProperty, 20d);
                panelFactory.SetValue(VirtualizingCanvas.ItemHeightProperty, 20d);
                panelFactory.SetValue(VirtualizingCanvas.ContentWidthProperty, 200d);
                panelFactory.SetValue(VirtualizingCanvas.ContentHeightProperty, 2000d);
                var listBox = new ListBox
                {
                    Width = 100,
                    Height = 100,
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, 100)
                        .Select(index => index % 2 == 0
                            ? new TestCanvasItem(160, index * 20d)
                            : new TestCanvasItem(0, (index / 2) * 20d))
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                var window = new Window
                {
                    Width = 100,
                    Height = 100,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = listBox
                };
                window.Show();
                listBox.UpdateLayout();

                var panel = FindVisualChild<VirtualizingCanvas>(listBox);
                Assert.NotNull(panel);
                realizedCount = VisualTreeHelper.GetChildrenCount(panel);
                window.Close();
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
        Assert.InRange(realizedCount, 1, 10);
    }

    [Fact]
    public void VirtualizingCanvas_ReattachesRecycledRowsAfterViewportExpands()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                const double cellSize = 40;
                const int columns = 10;
                const int rows = 4;
                var panelFactory = new FrameworkElementFactory(typeof(VirtualizingCanvas));
                panelFactory.SetValue(VirtualizingCanvas.ItemWidthProperty, cellSize);
                panelFactory.SetValue(VirtualizingCanvas.ItemHeightProperty, cellSize);
                panelFactory.SetValue(VirtualizingCanvas.ContentWidthProperty, columns * cellSize);
                panelFactory.SetValue(VirtualizingCanvas.ContentHeightProperty, rows * cellSize);
                var listBox = new ListBox
                {
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, columns * rows)
                        .Select(index => new TestCanvasItem(
                            (index % columns) * cellSize,
                            (index / columns) * cellSize))
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Disabled);
                listBox.SetValue(
                    ScrollViewer.VerticalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Disabled);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                listBox.SetValue(
                    VirtualizingPanel.VirtualizationModeProperty,
                    VirtualizationMode.Recycling);

                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(listBox, 1);
                root.Children.Add(listBox);
                window = new Window
                {
                    Width = columns * cellSize,
                    Height = 30 + (rows * cellSize),
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = root
                };
                window.Show();
                root.UpdateLayout();

                var panel = FindVisualChild<VirtualizingCanvas>(listBox);
                Assert.NotNull(panel);
                Assert.Equal(columns * rows, VisualTreeHelper.GetChildrenCount(panel));

                // A roll-up animation constrains the viewport and recycles the lower rows.
                window.Height = 30 + (2 * cellSize);
                root.UpdateLayout();
                Assert.True(VisualTreeHelper.GetChildrenCount(panel) < columns * rows);

                // Expanding must reattach all recycled containers, not merely update extent.
                window.Height = 30 + (rows * cellSize);
                panel.InvalidateMeasure();
                listBox.InvalidateMeasure();
                root.InvalidateMeasure();
                root.UpdateLayout();

                Assert.Equal(columns * rows, VisualTreeHelper.GetChildrenCount(panel));
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
    }

    [Theory]
    [InlineData(80, 5, 50, 150, 5, 29)]
    [InlineData(4, 3, 50, 500, 0, 3)]
    public void CenteredUniformPanel_CalculatesOnlyVisibleRows(
        int itemCount,
        int columns,
        double cellSize,
        double viewportHeight,
        int expectedFirstIndex,
        int expectedLastIndex)
    {
        var range = CenteredUniformPanel.CalculateVisibleItemRange(
            itemCount,
            columns,
            cellSize,
            verticalOffset: 100,
            viewportHeight,
            overscanRows: 1);

        Assert.Equal(expectedFirstIndex, range.FirstIndex);
        Assert.Equal(expectedLastIndex, range.LastIndex);
    }

    [Fact]
    public void CenteredUniformPanel_RealizesOnlyVisibleRows()
    {
        Exception? threadException = null;
        var realizedCount = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var panelFactory = new FrameworkElementFactory(typeof(CenteredUniformPanel));
                panelFactory.SetValue(CenteredUniformPanel.ColumnsProperty, 5);
                panelFactory.SetValue(CenteredUniformPanel.CellSizeProperty, 20d);
                var listBox = new ListBox
                {
                    Width = 100,
                    Height = 100,
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, 100)
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                var window = new Window
                {
                    Width = 100,
                    Height = 100,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = listBox
                };
                window.Show();
                listBox.UpdateLayout();

                var panel = FindVisualChild<CenteredUniformPanel>(listBox);
                Assert.NotNull(panel);
                realizedCount = VisualTreeHelper.GetChildrenCount(panel);
                window.Close();
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
        Assert.InRange(realizedCount, 1, 40);
    }

    [Fact]
    public void CenteredUniformPanel_KeepsLastRowInsideViewportWhenGridHasExtraWidth()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panelFactory = new FrameworkElementFactory(typeof(CenteredUniformPanel));
                panelFactory.SetValue(CenteredUniformPanel.ColumnsProperty, 2);
                panelFactory.SetValue(CenteredUniformPanel.CellSizeProperty, 37d);
                var listBox = new ListBox
                {
                    Width = 90,
                    Height = 76,
                    BorderThickness = new Thickness(0),
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, 4)
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                window = new Window
                {
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = listBox
                };
                window.Show();
                listBox.UpdateLayout();

                var panel = FindVisualChild<CenteredUniformPanel>(listBox);
                Assert.NotNull(panel);
                Assert.Equal(4, VisualTreeHelper.GetChildrenCount(panel));
                var lastRowBottom = Enumerable.Range(0, 4)
                    .Select(index => (FrameworkElement)VisualTreeHelper.GetChild(panel, index))
                    .Max(child => child.TransformToAncestor(panel)
                        .TransformBounds(new Rect(child.RenderSize))
                        .Bottom);

                Assert.True(
                    lastRowBottom <= panel.ActualHeight + 0.1,
                    $"Last row ends at {lastRowBottom:0.##}, outside the {panel.ActualHeight:0.##}-pixel viewport.");
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
    }

    [Fact]
    public void CenteredUniformPanel_UsesIndependentCellHeightForFileNameRows()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panelFactory = new FrameworkElementFactory(typeof(CenteredUniformPanel));
                panelFactory.SetValue(CenteredUniformPanel.ColumnsProperty, 2);
                panelFactory.SetValue(CenteredUniformPanel.CellSizeProperty, 40d);
                panelFactory.SetValue(CenteredUniformPanel.CellHeightProperty, 50d);
                var listBox = new ListBox
                {
                    Width = 80,
                    Height = 100,
                    BorderThickness = new Thickness(0),
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, 4)
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                window = new Window
                {
                    Width = 80,
                    Height = 100,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = listBox
                };
                window.Show();
                listBox.UpdateLayout();

                var panel = FindVisualChild<CenteredUniformPanel>(listBox);
                Assert.NotNull(panel);
                var lastItem = (FrameworkElement)VisualTreeHelper.GetChild(panel, 3);
                var lastBounds = lastItem.TransformToAncestor(panel)
                    .TransformBounds(new Rect(lastItem.RenderSize));

                Assert.Equal(50, lastBounds.Top, 3);
                Assert.Equal(100, lastBounds.Bottom, 3);
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
    }

    [Fact]
    public void CenteredUniformPanel_AlignsPartialLastRowToTheLeft()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panelFactory = new FrameworkElementFactory(typeof(CenteredUniformPanel));
                panelFactory.SetValue(CenteredUniformPanel.ColumnsProperty, 4);
                panelFactory.SetValue(CenteredUniformPanel.CellSizeProperty, 25d);
                var listBox = new ListBox
                {
                    Width = 100,
                    Height = 60,
                    BorderThickness = new Thickness(0),
                    ItemsPanel = new ItemsPanelTemplate(panelFactory),
                    ItemsSource = Enumerable.Range(0, 5)
                };
                listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
                listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                window = new Window
                {
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = listBox
                };
                window.Show();
                listBox.UpdateLayout();

                var panel = FindVisualChild<CenteredUniformPanel>(listBox);
                Assert.NotNull(panel);
                Assert.Equal(5, VisualTreeHelper.GetChildrenCount(panel));
                var bounds = Enumerable.Range(0, 5)
                    .Select(index => (FrameworkElement)VisualTreeHelper.GetChild(panel, index))
                    .Select(child => child.TransformToAncestor(panel)
                        .TransformBounds(new Rect(child.RenderSize)))
                    .ToArray();

                Assert.Equal(bounds[0].Left, bounds[4].Left, 3);
                Assert.True(
                    bounds[4].Left < panel.ActualWidth / 2,
                    $"Partial last row starts at {bounds[4].Left:0.##}, expected left alignment.");
            }
            catch (Exception exception)
            {
                threadException = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadException);
    }

    [Fact]
    public void DrawerItemViewModel_DelaysIconLoadingUntilAVisibleContainerRequestsIt()
    {
        var item = CreateDrawerItem("lazy.txt", 0, 0, new DesktopBoxLayoutSettings());

        Assert.False(item.IsIconLoadRequested);

        item.RequestIconSize(48);

        Assert.False(item.IsIconLoadRequested);

        item.EnsureIconLoaded();

        Assert.True(item.IsIconLoadRequested);
    }

    [Fact]
    public void ResettableObservableCollection_ReplacesItemsWithOneResetNotification()
    {
        var collection = new ResettableObservableCollection<int> { 1, 2, 3 };
        var notifications = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => notifications.Add(args.Action);

        collection.ReplaceAll([4, 5]);

        Assert.Equal([4, 5], collection);
        Assert.Equal([NotifyCollectionChangedAction.Reset], notifications);
    }

    [Fact]
    public void ResettableObservableCollection_CanReplaceFromItself()
    {
        var collection = new ResettableObservableCollection<int> { 1, 2, 3 };

        collection.ReplaceAll(collection);

        Assert.Equal([1, 2, 3], collection);
    }

    private static DrawerItemViewModel CreateDrawerItem(
        string name,
        int column,
        int row,
        DesktopBoxLayoutSettings settings)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItemViewModel(
            new DrawerItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                name,
                ItemKind.File,
                SourcePath: null,
                StoredPath: null,
                SortOrder: 0,
                CreatedAt: now,
                UpdatedAt: now),
            iconPixelSize: 16);
        item.SetGridPosition(column, row, settings);
        return item;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private sealed class TestCanvasItem(double left, double top) : IVirtualizingCanvasItem
    {
        public double VirtualizationLeft { get; } = left;

        public double VirtualizationTop { get; } = top;
    }
}
