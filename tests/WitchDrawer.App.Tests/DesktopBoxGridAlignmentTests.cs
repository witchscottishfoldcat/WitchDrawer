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

    private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out var current))
        {
            if (current is T match)
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
        BoxType type, string preset, bool titleVisible, bool namesVisible, bool fixedSize)
    {
        // Layout only: these services are never initialized and no user data is read/written.
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerLayout", Guid.NewGuid().ToString("N")));
        var repository = new DrawerRepository(paths.DatabasePath);
        var now = DateTimeOffset.UtcNow;
        var box = new Box(Guid.NewGuid(), "布局测试", type, null, 0, now, now);
        var layout = new DesktopBoxLayoutSettings(type == BoxType.Drawer);
        layout.ApplyPresetWithoutCallback(preset);
        var model = new DesktopBoxViewModel(box, new DrawerService(paths, repository),
            new TodoService(repository), new NoOpLauncher(), NullAppLogger.Instance,
            BoxVisualStyle.Modern, layout);
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
        VisualTreeHelper.SetRootDpi(window, new DpiScale(dpi, dpi));
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        return window;
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
