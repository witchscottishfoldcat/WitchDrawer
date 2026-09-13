using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class BoxControlsLayoutTests
{
    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.25, true)]
    [InlineData(1.5, false)]
    [InlineData(2.0, true)]
    public void SizeControls_FitInsideTheAnimatedPageHost(double scale, bool fixedSize)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var card = LoadControlsCard();
                card.DataContext = new
                {
                    SelectedBox = new { SupportsFixedSize = true, CanSelectVisualStyle = true, TypeLabel = "普通" },
                    BoxSizeSettings = new { IsAdaptiveMode = !fixedSize, IsFixedMode = fixedSize, FixedColumns = 5, FixedRows = 5 }
                };
                card.LayoutTransform = new ScaleTransform(scale, scale);
                card.Measure(new Size(700 * scale, double.PositiveInfinity));
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();

                var host = Find<Grid>(card, element => element.Name == "BoxControlsPageHost");
                var primary = Find<Grid>(card, element => element.Name == "BoxControlsPrimaryPanel");
                var panelBounds = primary.TransformToAncestor(host).TransformBounds(new Rect(primary.RenderSize));
                Assert.True(panelBounds.Top >= -0.01 && panelBounds.Bottom <= host.ActualHeight + 0.01,
                    $"Primary controls {panelBounds} exceed host height {host.ActualHeight} at {scale}x.");
                foreach (var label in new[] { "自适应", "固定格数" })
                {
                    var button = Find<Button>(card, element => Equals(element.Content, label));
                    var bounds = button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(bounds.Bottom <= host.ActualHeight + 0.01,
                        $"{label} ends at {bounds.Bottom}, below host height {host.ActualHeight}.");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF layout test timed out.");
        Assert.Null(failure);
    }

    private static Border LoadControlsCard()
    {
        var sourceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "WitchDrawer.App"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var main = XDocument.Load(Path.Combine(sourceDirectory, "MainWindow.xaml"));
        var card = new XElement(main.Descendants(p + "Grid")
            .Single(element => (string?)element.Attribute(x + "Name") == "BoxControlsPageHost")
            .Parent!);
        foreach (var attribute in main.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var value = attribute.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)
                ? attribute.Value + ";assembly=WitchDrawer.App" : attribute.Value;
            card.SetAttributeValue(attribute.Name, value);
        }

        card.SetAttributeValue(XNamespace.Xmlns + "infra", "clr-namespace:WitchDrawer.App.Infrastructure;assembly=WitchDrawer.App");
        card.DescendantsAndSelf().Attributes()
            .Where(attribute => attribute.Name.Namespace == XNamespace.None && attribute.Value.StartsWith("On", StringComparison.Ordinal))
            .Remove();
        var app = XDocument.Load(Path.Combine(sourceDirectory, "App.xaml"));
        card.AddFirst(new XElement(p + "Border.Resources", app.Root!.Element(p + "Application.Resources")!.Elements()));
        foreach (var element in card.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)
                && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=WitchDrawer.App");
            }
        }

        return (Border)XamlReader.Parse(card.ToString());
    }

    private static T Find<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(parent);
        while (queue.TryDequeue(out var item))
        {
            if (item is T match && predicate(match))
            {
                return match;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(item, index));
            }
        }

        throw new InvalidOperationException($"Missing {typeof(T).Name} in controls card.");
    }
}
