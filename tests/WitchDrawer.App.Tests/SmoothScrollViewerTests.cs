using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using WitchDrawer.App.Controls;

namespace WitchDrawer.App.Tests;

public sealed class SmoothScrollViewerTests
{
    [Fact]
    public Task List_WheelUsesPixelDistanceAndTrackPagesThroughVirtualizedItems() => RunOnStaAsync(async () =>
    {
        var list = new ListBox
        {
            Resources = LoadScrollStyles(),
            ItemsSource = Enumerable.Range(0, 150),
            ItemTemplate = (DataTemplate)XamlReader.Parse("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <TextBlock Text="{Binding}" Height="144" />
                </DataTemplate>
                """)
        };
        list.SetResourceReference(FrameworkElement.StyleProperty, "SmoothListBoxStyle");
        using var source = new HwndSource(new HwndSourceParameters("Smooth scroll test")
        {
            Width = 340, Height = 290, WindowStyle = 0
        });
        source.RootVisual = list;
        list.Measure(new Size(340, 290));
        list.Arrange(new Rect(0, 0, 340, 290));
        list.UpdateLayout();

        var viewer = Assert.Single(Descendants<SmoothScrollViewer>(list));
        Assert.True(viewer.ScrollableHeight > 1000);
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 12);
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -30)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent
        };
        var firstItem = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        firstItem.RaiseEvent(wheel);
        Assert.True(wheel.Handled);
        var lines = SystemParameters.WheelScrollLines;
        var wheelDistance = lines < 0 ? viewer.ViewportHeight / 4 : lines * 12d / 4;
        await WaitForOffsetAsync(viewer, wheelDistance);

        var scrollbar = Assert.Single(Descendants<ScrollBar>(viewer), bar => bar.Orientation == Orientation.Vertical);
        var track = Assert.IsType<Track>(scrollbar.Template.FindName("PART_Track", scrollbar));
        var pageButton = track.IncreaseRepeatButton;
        var pageDown = Assert.IsType<RoutedCommand>(pageButton.Command);
        Assert.True(pageDown.CanExecute(null, pageButton));
        var pageTarget = viewer.VerticalOffset + viewer.ViewportHeight;
        pageDown.Execute(null, pageButton);
        await WaitForOffsetAsync(viewer, pageTarget);
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 12);
    });

    private static async Task WaitForOffsetAsync(ScrollViewer viewer, double expected)
    {
        var started = Stopwatch.GetTimestamp();
        while (Math.Abs(viewer.VerticalOffset - expected) > 1 &&
               Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3))
            await Task.Delay(15);
        Assert.InRange(viewer.VerticalOffset, expected - 1, expected + 1);
    }

    private static ResourceDictionary LoadScrollStyles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace controls = "clr-namespace:WitchDrawer.App.Controls";
        XNamespace referencedControls = controls.NamespaceName + ";assembly=WitchDrawer.App";
        var styles = XDocument.Load(Path.Combine(directory.FullName, "src", "WitchDrawer.App", "App.xaml"))
            .Root!.Element(presentation + "Application.Resources")!.Elements(presentation + "Style")
            .Where(style => (string?)style.Attribute("TargetType") == "{x:Type ListBox}" ||
                            (string?)style.Attribute(xaml + "Key") is "DrawerScrollBarStyle" or "SmoothListBoxStyle");
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute("xmlns", presentation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "controls", referencedControls.NamespaceName),
            styles);
        foreach (var element in dictionary.Descendants().Where(element => element.Name.Namespace == controls))
            element.Name = referencedControls + element.Name.LocalName;
        return Assert.IsType<ResourceDictionary>(XamlReader.Parse(dictionary.ToString()));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Task RunOnStaAsync(Func<Task> test)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); finished.SetResult(); }
                catch (Exception exception) { finished.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
