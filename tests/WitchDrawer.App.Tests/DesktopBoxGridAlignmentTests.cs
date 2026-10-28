using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Controls;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxGridAlignmentTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void MappingListInsertionLine_AlignsOnTheFirstVisibleLayout(double dpi)
    {
        RunOnSta(() =>
        {
            var model = CreateViewModel(BoxType.Mapping, "6x6", true, false, false);
            typeof(DesktopBoxViewModel).GetMethod("SetMappingListMode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(model, [true]);
            var window = LoadWindow(model, dpi);
            try
            {
                var list = (ListBox)window.FindName("FileList");
                var overlay = (MappingListDropCanvas)window.FindName("MappingListDropOverlay");
                var indicator = (Border)window.FindName("MappingListInsertionIndicator");
                var row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(1);
                foreach (var after in new[] { false, true })
                {
                    overlay.Visibility = Visibility.Collapsed;
                    UpdateLayout(window);
                    overlay.ShowBoundary(row, after);
                    // One layout pass is sufficient; a second DragOver must not
                    // be required to correct the old header-height displacement.
                    UpdateLayout(window);
                    var expected = row.TranslatePoint(new Point(0, after ? row.ActualHeight : 0), overlay);
                    var actual = indicator.TranslatePoint(new Point(), overlay);
                    Assert.True(Math.Abs(actual.Y - (expected.Y - 1)) * dpi < 0.51,
                        $"First insertion line is displaced by {actual.Y - expected.Y + 1} DIP at {dpi} DPI.");
                    var listOrigin = list.TranslatePoint(new Point(), overlay);
                    Assert.True(Math.Abs(actual.X - listOrigin.X - Math.Max(6, list.Padding.Left)) * dpi < 0.51);
                    var beforeRepeat = actual;
                    overlay.ShowBoundary(row, after);
                    UpdateLayout(window);
                    Assert.Equal(beforeRepeat, indicator.TranslatePoint(new Point(), overlay));
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void AdaptivePreview_CancelKeepsTheOriginAndCompletedGrowthStillClamps(double dpi)
    {
        RunOnSta(() =>
        {
            var model = CreateViewModel(BoxType.Normal, "6x6", true, false, false);
            var window = LoadWindow(model, dpi);
            try
            {
                var border = (Border)window.FindName("WindowBorder");
                var workArea = new Rect(0, 0, 2000, 2000);
                var originalSize = new Size(border.ActualWidth * dpi, border.ActualHeight * dpi);
                var origin = new Point(workArea.Right - originalSize.Width, workArea.Bottom - originalSize.Height);
                model.ShowDragPreview(2, 4);
                UpdateLayout(window);
                var previewSize = new Size(border.ActualWidth * dpi, border.ActualHeight * dpi);
                Assert.True(previewSize.Width > originalSize.Width && previewSize.Height > originalSize.Height);
                Assert.Null(DesktopBoxWindow.GetWorkAreaCorrection(new Rect(origin, previewSize),
                    workArea, model.IsDragPreviewVisible));

                model.HideDragPreview();
                UpdateLayout(window);
                Assert.Equal(originalSize, new Size(border.ActualWidth * dpi, border.ActualHeight * dpi));
                Assert.Null(DesktopBoxWindow.GetWorkAreaCorrection(new Rect(origin, originalSize),
                    workArea, model.IsDragPreviewVisible));

                // A real completed drop increases the item extent independently
                // of the preview, and must receive the usual work-area correction.
                model.Items[0].SetGridPosition(2, 4, model.LayoutSettings);
                model.ShowDragPreview(2, 4);
                model.HideDragPreview();
                UpdateLayout(window);
                var finalSize = new Size(border.ActualWidth * dpi, border.ActualHeight * dpi);
                var correction = DesktopBoxWindow.GetWorkAreaCorrection(new Rect(origin, finalSize),
                    workArea, model.IsDragPreviewVisible);
                Assert.NotNull(correction);
                Assert.Equal(workArea.Right - finalSize.Width, correction.Value.X, precision: 5);
                Assert.Equal(workArea.Bottom - finalSize.Height, correction.Value.Y, precision: 5);
            }
            finally { window.Close(); }
        });
    }

    public static IEnumerable<object[]> DragPreviewCases()
    {
        foreach (var preset in new[] { "3x3", "4x4", "5x5", "6x6" })
        foreach (var namesVisible in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        foreach (var visualStyle in new[] { BoxVisualStyle.Modern, BoxVisualStyle.Pixel })
        {
            yield return new object[] { preset, namesVisible, dpi, visualStyle };
        }
    }

    [Theory]
    [MemberData(nameof(DragPreviewCases))]
    public void GridDragPreview_MatchesTheIconCardBounds(
        string preset, bool namesVisible, double dpi, BoxVisualStyle visualStyle)
    {
        RunOnSta(() =>
        {
            var model = CreateViewModel(BoxType.Normal, preset, true, namesVisible, true, visualStyle);
            model.LayoutSettings.ApplyCornerScale(1.5);
            // A nonzero column and row catch origin errors as well as incorrect sizing.
            model.ShowDragPreview(1, 2);
            var window = LoadWindow(model, dpi);
            try
            {
                var list = (ListBox)window.FindName("IconList");
                var item = model.Items.Single(item => item.GridColumn == 1 && item.GridRow == 2);
                var container = Assert.IsAssignableFrom<FrameworkElement>(list.ItemContainerGenerator.ContainerFromItem(item));
                var card = FindDescendant<Border>(container, border => border.Name == "IconBorder");
                Assert.Equal(dpi, VisualTreeHelper.GetDpi(card).DpiScaleX, precision: 5);
                var canvas = (Canvas)window.FindName("DragPreviewCanvas");
                var outline = FindDescendant<System.Windows.Shapes.Rectangle>(canvas);

                AssertMatchingBounds(card, outline, canvas, dpi);
                Assert.Equal(card.CornerRadius.TopLeft, outline.RadiusX, precision: 5);
                Assert.Equal(card.CornerRadius.TopLeft, outline.RadiusY, precision: 5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    public static IEnumerable<object[]> DrawerDragPreviewCases()
    {
        foreach (var preset in new[] { "3x3", "4x4", "5x5", "6x6" })
        foreach (var namesVisible in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        {
            yield return new object[] { preset, namesVisible, dpi, BoxVisualStyle.Modern };
        }
        yield return new object[] { "6x6", true, 1.25, BoxVisualStyle.Pixel };
    }

    [Theory]
    [MemberData(nameof(DrawerDragPreviewCases))]
    public void DrawerCoverDragPreview_MatchesTheIconCardBounds(
        string preset, bool namesVisible, double dpi, BoxVisualStyle visualStyle)
    {
        RunOnSta(() =>
        {
            var model = CreateViewModel(BoxType.Drawer, preset, true, namesVisible, false, visualStyle);
            var window = LoadWindow(model, dpi);
            try
            {
                const int cellIndex = 3;
                var cover = (ItemsControl)window.FindName("DrawerCoverItems");
                var tile = (FrameworkElement)cover.ItemContainerGenerator.ContainerFromIndex(cellIndex);
                var button = FindDescendant<Button>(tile, button => button.Visibility == Visibility.Visible);
                var card = (Border)((StackPanel)button.Content).Children[0];
                var canvas = (Canvas)window.FindName("DragPreviewCanvas");
                var cell = DesktopBoxWindow.CalculateCoverCellRect(cellIndex, model.DrawerCoverDisplayColumns,
                    model.LayoutSettings.DrawerCoverCellWidth, model.LayoutSettings.DrawerCoverCellHeight);
                model.ShowDragPreviewAt(cell.X, cell.Y, cell.Width, cell.Height);
                UpdateLayout(window);
                var outline = FindDescendant<System.Windows.Shapes.Rectangle>(canvas);
                Assert.Equal(dpi, VisualTreeHelper.GetDpi(card).DpiScaleX, precision: 5);
                AssertMatchingBounds(card, outline, canvas, dpi);
                Assert.Equal(card.CornerRadius.TopLeft, outline.RadiusX, precision: 5);
                Assert.Equal(card.CornerRadius.TopLeft, outline.RadiusY, precision: 5);
                var coverSurface = VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(cover));
                Assert.True(Panel.GetZIndex(canvas) > Panel.GetZIndex((UIElement)coverSurface),
                    "The fitted preview must render above the cover's icon card.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true, false, BoxVisualStyle.Modern, "empty.txt", 1.25)]
    [InlineData(true, true, BoxVisualStyle.Pixel, "报告（终稿）.txt", 1.5)]
    [InlineData(false, true, BoxVisualStyle.Modern, "报告（终稿）.txt", 1.25)]
    public void GridDragPreview_EmptyTargetMatchesTheCardAfterAnItemArrives(
        bool emptyBox, bool namesVisible, BoxVisualStyle visualStyle, string fileName, double dpi)
    {
        RunOnSta(() =>
        {
            var model = CreateViewModel(BoxType.Normal, "6x6", true, namesVisible, true, visualStyle);
            if (emptyBox) model.Items.Clear();
            model.ApplySizeMode(BoxSizeModeState.Adaptive);
            var window = LoadWindow(model, dpi);
            try
            {
                var column = emptyBox ? 0 : 3;
                var row = emptyBox ? 0 : 4;
                var previousWidth = model.GridCanvasWidth;
                // Begin with a hidden overlay and no container at the destination.
                model.ShowDragPreview(column, row);
                UpdateLayout(window, dpi);
                if (!emptyBox) Assert.True(model.GridCanvasWidth > previousWidth);
                var canvas = (Canvas)window.FindName("DragPreviewCanvas");
                var outline = FindDescendant<System.Windows.Shapes.Rectangle>(canvas);
                var before = new Rect(outline.TranslatePoint(new Point(), canvas), outline.RenderSize);
                var now = DateTimeOffset.UtcNow;
                var item = new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), model.BoxId, fileName,
                    ItemKind.File, null, null, model.Items.Count, now, now, column, row));
                item.UpdateCanvasPosition(model.LayoutSettings);
                model.Items.Add(item);
                UpdateLayout(window, dpi);

                var list = (ListBox)window.FindName("IconList");
                var container = Assert.IsAssignableFrom<FrameworkElement>(list.ItemContainerGenerator.ContainerFromItem(item));
                var card = FindDescendant<Border>(container, border => border.Name == "IconBorder");
                AssertMatchingBounds(card, outline, canvas, dpi);
                AssertMatchingBounds(before,
                    new Rect(outline.TranslatePoint(new Point(), canvas), outline.RenderSize), dpi);
                model.HideDragPreview();
                UpdateLayout(window);
                Assert.Equal(Visibility.Collapsed, canvas.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DrawerCoverDragPreview_EmptyCoverMatchesTheFirstCard()
    {
        RunOnSta(() =>
        {
            const double dpi = 1.25;
            var model = CreateViewModel(BoxType.Drawer, "6x6", true, true, false, BoxVisualStyle.Pixel);
            model.Items.Clear();
            model.DrawerCoverTiles.Clear();
            var window = LoadWindow(model, dpi);
            try
            {
                var cell = DesktopBoxWindow.CalculateCoverCellRect(0, model.DrawerCoverDisplayColumns,
                    model.LayoutSettings.DrawerCoverCellWidth, model.LayoutSettings.DrawerCoverCellHeight);
                model.ShowDragPreviewAt(cell.X, cell.Y, cell.Width, cell.Height);
                UpdateLayout(window, dpi);
                var canvas = (Canvas)window.FindName("DragPreviewCanvas");
                var outline = FindDescendant<System.Windows.Shapes.Rectangle>(canvas);
                var before = new Rect(outline.TranslatePoint(new Point(), canvas), outline.RenderSize);
                var now = DateTimeOffset.UtcNow;
                var item = new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), model.BoxId, "报告.txt",
                    ItemKind.File, null, null, 0, now, now, 0, 0));
                item.UpdateCanvasPosition(model.LayoutSettings);
                model.Items.Add(item);
                model.DrawerCoverTiles.Add(DrawerCoverTileViewModel.ForItem(item));
                UpdateLayout(window, dpi);
                var cover = (ItemsControl)window.FindName("DrawerCoverItems");
                var tile = (FrameworkElement)cover.ItemContainerGenerator.ContainerFromIndex(0);
                var button = FindDescendant<Button>(tile, button => button.Visibility == Visibility.Visible);
                var card = (Border)((StackPanel)button.Content).Children[0];

                AssertMatchingBounds(card, outline, canvas, dpi);
                AssertMatchingBounds(before,
                    new Rect(outline.TranslatePoint(new Point(), canvas), outline.RenderSize), dpi);
                Assert.Equal(card.CornerRadius.TopLeft, outline.RadiusX, precision: 5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertMatchingBounds(
        FrameworkElement card, FrameworkElement outline, Visual relativeTo, double dpi)
    {
        var expected = new Rect(card.TranslatePoint(new Point(), (UIElement)relativeTo), card.RenderSize);
        var actual = new Rect(outline.TranslatePoint(new Point(), (UIElement)relativeTo), outline.RenderSize);
        AssertMatchingBounds(expected, actual, dpi);
    }

    private static void AssertMatchingBounds(Rect expected, Rect actual, double dpi)
    {
        foreach (var (edge, expectedValue, actualValue) in new[]
                 {
                     ("left", expected.Left, actual.Left), ("top", expected.Top, actual.Top),
                     ("right", expected.Right, actual.Right), ("bottom", expected.Bottom, actual.Bottom)
                 })
        {
            Assert.True(Math.Abs(expectedValue - actualValue) * dpi < 0.51,
                $"Preview {edge} edge {actualValue} does not match icon card {expectedValue} DIP at {dpi} DPI.");
        }
    }

    private static void UpdateLayout(Window window, double? dpi = null)
    {
        var content = (FrameworkElement)window.Content;
        InvalidateLayoutTree(content);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        if (dpi is { } scale)
        {
            // New item templates initially inherit the monitor DPI. Apply the
            // test scale after realization, then lay out their new text visuals.
            VisualTreeHelper.SetRootDpi(window, new DpiScale(scale, scale));
            if (VisualTreeHelper.GetParent(content) is null)
            {
                VisualTreeHelper.SetRootDpi(content, new DpiScale(scale, scale));
            }
            UpdateLayout(window);
        }
    }

    private static void InvalidateLayoutTree(DependencyObject visual)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        {
            InvalidateLayoutTree(VisualTreeHelper.GetChild(visual, index));
        }
        if (visual is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
        }
    }

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Drag preview layout test timed out.");
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void DrawerCover_FirstTileStaysPixelAlignedAcrossResize(double dpi)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = CreateViewModel(BoxType.Drawer, "6x6", true, false, false);
                window = LoadWindow(model, dpi);
                var content = (FrameworkElement)window.Content;
                var coverItems = (ItemsControl)window.FindName("DrawerCoverItems");
                var border = (FrameworkElement)window.FindName("WindowBorder");
                var firstTile = (FrameworkElement)coverItems.ItemContainerGenerator.ContainerFromIndex(0);
                var before = firstTile.TranslatePoint(new Point(), border);
                var firstFrame = (Border)((StackPanel)FindDescendant<Button>(firstTile).Content).Children[0];
                var frameBefore = firstFrame.TranslatePoint(new Point(), border);
                var secondTile = (FrameworkElement)coverItems.ItemContainerGenerator.ContainerFromIndex(1);
                var secondFrame = (Border)((StackPanel)FindDescendant<Button>(secondTile).Content).Children[0];
                var secondBefore = secondFrame.TranslatePoint(new Point(), border);

                var inset = DesktopBoxLayoutSettings.DrawerSurfaceInset * 2;
                model.BeginDrawerCoverResize();
                model.PreviewDrawerCoverResize(
                    (3 * model.LayoutSettings.DrawerCoverCellWidth) + inset,
                    (4 * model.LayoutSettings.DrawerCoverCellHeight) + inset);
                content.InvalidateMeasure();
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                content.Arrange(new Rect(content.DesiredSize));
                content.UpdateLayout();
                var previewSecond = secondFrame.TranslatePoint(new Point(), border);
                Assert.Equal(2, model.DrawerCoverColumns);
                Assert.Equal(3, model.DrawerCoverDisplayColumns);
                Assert.True(Math.Abs(secondBefore.X - previewSecond.X) * dpi < 0.5,
                    $"Second icon shifted during resize preview from {secondBefore.X} to {previewSecond.X} DIP at {dpi} DPI.");

                model.EndDrawerCoverResize(commit: true);
                content.InvalidateMeasure();
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                content.Arrange(new Rect(content.DesiredSize));
                content.UpdateLayout();

                var afterTile = (FrameworkElement)coverItems.ItemContainerGenerator.ContainerFromIndex(0);
                var after = afterTile.TranslatePoint(new Point(), border);
                var frameAfter = firstFrame.TranslatePoint(new Point(), border);
                var secondAfter = secondFrame.TranslatePoint(new Point(), border);
                Assert.Same(firstTile, afterTile);
                Assert.True(Math.Abs(before.X - after.X) * dpi < 0.5,
                    $"First tile shifted horizontally from {before.X} to {after.X} DIP at {dpi} DPI.");
                Assert.True(Math.Abs(before.Y - after.Y) * dpi < 0.5,
                    $"First tile shifted vertically from {before.Y} to {after.Y} DIP at {dpi} DPI.");
                Assert.True(Math.Abs(frameBefore.X - frameAfter.X) * dpi < 0.5,
                    $"First icon shifted horizontally from {frameBefore.X} to {frameAfter.X} DIP at {dpi} DPI.");
                Assert.True(Math.Abs(frameBefore.Y - frameAfter.Y) * dpi < 0.5,
                    $"First icon shifted vertically from {frameBefore.Y} to {frameAfter.Y} DIP at {dpi} DPI.");
                Assert.True(Math.Abs(secondBefore.X - secondAfter.X) * dpi < 0.5,
                    $"Second icon shifted horizontally from {secondBefore.X} to {secondAfter.X} DIP at {dpi} DPI.");
                Assert.True(Math.Abs(secondBefore.Y - secondAfter.Y) * dpi < 0.5,
                    $"Second icon shifted vertically from {secondBefore.Y} to {secondAfter.Y} DIP at {dpi} DPI.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Drawer cover layout test timed out.");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("6x6", 1, false)]
    [InlineData("6x6", 2, false)]
    [InlineData("6x6", 3, false)]
    [InlineData("6x6", 4, false)]
    [InlineData("5x5", 3, false)]
    [InlineData("4x4", 3, false)]
    [InlineData("3x3", 3, false)]
    [InlineData("6x6", 3, true)]
    public void SecondaryPopup_UsesEqualInsetsAroundTheGrid(string preset, int itemCount, bool namesVisible)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = CreateViewModel(BoxType.Drawer, preset, false, namesVisible, false);
                foreach (var item in model.Items.Take(itemCount))
                {
                    model.DrawerSecondaryItems.Add(item);
                }

                window = LoadWindow(model, 1.5);
                var popup = (Border)window.FindName("DrawerSecondaryPopupRoot");
                popup.DataContext = model;
                // The closed popup is a separate visual root; fix its DPI so
                // template padding can be compared without per-edge rounding.
                VisualTreeHelper.SetRootDpi(popup, new DpiScale(1, 1));
                // Measure the real popup template without opening it or taking focus.
                popup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                popup.Arrange(new Rect(popup.DesiredSize));
                popup.UpdateLayout();
                var panel = FindDescendant<CenteredUniformPanel>(popup);
                var origin = panel.TranslatePoint(new Point(), popup);
                var gridWidth = model.DrawerSecondaryColumns * model.LayoutSettings.ItemSlotWidth;
                var gridHeight = model.DrawerSecondaryRows * model.LayoutSettings.ItemSlotHeight;
                Assert.Equal(gridWidth + DesktopBoxViewModel.DrawerSecondaryPanelChrome, popup.Width);
                Assert.Equal(gridHeight + DesktopBoxViewModel.DrawerSecondaryPanelChrome, popup.Height);
                Assert.InRange(Math.Abs(origin.X - origin.Y), 0, 1);
                Assert.InRange(Math.Abs(origin.X - (popup.ActualWidth - origin.X - gridWidth)), 0, 1);
                Assert.InRange(Math.Abs(origin.Y - (popup.ActualHeight - origin.Y - gridHeight)), 0, 1);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Popup layout test timed out.");
        Assert.Null(failure);
    }

    private static T FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out var current))
        {
            if (current is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
            }
        }

        throw new InvalidOperationException($"Missing {typeof(T).Name} in popup.");
    }

    public static IEnumerable<object[]> LayoutCases()
    {
        foreach (var preset in new[] { "3x3", "4x4", "5x5", "6x6" })
        foreach (var dpi in preset == "4x4" ? new[] { 1d, 1.25, 1.5, 2 } : new[] { 1.5 })
        foreach (var titleVisible in new[] { false, true })
        foreach (var namesVisible in new[] { false, true })
        foreach (var fixedSize in new[] { false, true })
        {
            yield return new object[] { preset, dpi, titleVisible, namesVisible, fixedSize };
        }
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void FourRows_HaveTheSameVisibleHeight(
        string preset, double dpi, bool titleVisible, bool namesVisible, bool fixedSize)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? normal = null;
            Window? drawer = null;
            try
            {
                normal = LoadWindow(CreateViewModel(BoxType.Normal, preset, titleVisible, namesVisible, fixedSize), dpi);
                drawer = LoadWindow(CreateViewModel(BoxType.Drawer, preset, titleVisible, namesVisible, fixedSize), dpi);
                var normalBorder = (Border)normal.FindName("WindowBorder");
                var drawerBorder = (Border)drawer.FindName("WindowBorder");
                Assert.Equal(normalBorder.ActualHeight, drawerBorder.ActualHeight, precision: 5);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                drawer?.Close();
                normal?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Layout test timed out.");
        Assert.Null(failure);
    }

    private static DesktopBoxViewModel CreateViewModel(
        BoxType type, string preset, bool titleVisible, bool namesVisible, bool fixedSize,
        BoxVisualStyle visualStyle = BoxVisualStyle.Modern)
    {
        // Layout only: these services are never initialized and no user data is read/written.
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerLayout", Guid.NewGuid().ToString("N")));
        var repository = new DrawerRepository(paths.DatabasePath);
        var now = DateTimeOffset.UtcNow;
        var box = new Box(Guid.NewGuid(), "布局测试", type, null, 0, now, now);
        var layout = new DesktopBoxLayoutSettings(type == BoxType.Drawer);
        layout.ApplyPresetWithoutCallback(preset);
        var model = new DesktopBoxViewModel(box, new DrawerService(paths, repository),
            new TodoService(repository), new NoOpLauncher(),
            new WitchDrawer.Native.Files.ShellChangeNotifierService(), NullAppLogger.Instance,
            visualStyle, layout);
        model.ApplyTitleVisibility(titleVisible);
        model.ApplyFileNameVisibility(namesVisible);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var item = new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), box.Id, "item",
                    ItemKind.File, null, null, row * 2 + column, now, now, column, row));
                item.UpdateCanvasPosition(layout);
                model.Items.Add(item);
            }
        }

        model.ApplySizeMode(new BoxSizeModeState(true, 2, 4));
        if (!fixedSize)
        {
            model.ApplySizeMode(BoxSizeModeState.Adaptive);
        }

        // Existing saved covers included 20 DIP of chrome. Loading them after
        // the correction must preserve the user's 2 columns / 4 rows.
        model.ResizeDrawerCover(2 * layout.ItemSlotWidth + 20, 4 * layout.ItemSlotHeight + 20);
        Assert.Equal(2, model.DrawerCoverColumns);
        Assert.Equal(4, model.DrawerCoverRows);
        return model;
    }

    private static Window LoadWindow(DesktopBoxViewModel model, double dpi)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "WitchDrawer.App"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = XDocument.Load(Path.Combine(source, "Views", "DesktopBoxWindow.xaml")).Root!;
        root.Attribute(x + "Class")!.Remove();
        root.Attribute("Icon")!.Remove();
        // Geometry uses the real templates; icon extraction is outside this test.
        root.DescendantsAndSelf().Attributes()
            .Where(attribute => attribute.Name.LocalName.StartsWith("DeferredIconLoad.", StringComparison.Ordinal))
            .Remove();
        root.DescendantsAndSelf().Attributes()
            .Where(attribute => attribute.Name.Namespace == XNamespace.None
                && attribute.Value.StartsWith("On", StringComparison.Ordinal)).Remove();
        foreach (var attribute in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            if (attribute.Value.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                attribute.Value += ";assembly=WitchDrawer.App";
            }
        }

        var appResources = XDocument.Load(Path.Combine(source, "App.xaml"))
            .Root!.Element(p + "Application.Resources")!;
        root.SetAttributeValue(XNamespace.Xmlns + "infra", "clr-namespace:WitchDrawer.App.Infrastructure;assembly=WitchDrawer.App");
        root.Element(p + "Window.Resources")!.Element(p + "ResourceDictionary")!
            .Add(appResources.Elements());
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)
                && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=WitchDrawer.App");
            }
        }

        var window = (Window)XamlReader.Parse(root.ToString());
        window.DataContext = model;
        new WindowInteropHelper(window).EnsureHandle();
        UpdateLayout(window, dpi);
        return window;
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
