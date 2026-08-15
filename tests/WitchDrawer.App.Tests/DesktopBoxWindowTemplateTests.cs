using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowTemplateTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void IconGridTemplate_CentersContentVertically()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var iconList = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element =>
                (string?)element.Attribute(XamlNamespace + "Name") == "IconList");
        var itemTemplate = Assert.Single(
            iconList.Elements(PresentationNamespace + "ListBox.ItemTemplate"));
        var dataTemplate = Assert.Single(
            itemTemplate.Elements(PresentationNamespace + "DataTemplate"));
        var templateRoot = Assert.Single(
            dataTemplate.Elements(),
            element => !element.Name.LocalName.Contains('.', StringComparison.Ordinal));

        Assert.Equal("StackPanel", templateRoot.Name.LocalName);
        Assert.Equal("Center", (string?)templateRoot.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void RollUpButton_KeepsHeaderAndCollapsesTheContentRow()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var rootGrid = Assert.Single(
            document.Descendants(PresentationNamespace + "Grid"),
            element => (string?)element.Attribute("MouseLeftButtonDown") == "OnSurfaceMouseLeftButtonDown");
        var rows = Assert.Single(rootGrid.Elements(PresentationNamespace + "Grid.RowDefinitions"));
        var definitions = rows.Elements(PresentationNamespace + "RowDefinition").ToArray();
        var button = Assert.Single(
            rootGrid.Descendants(PresentationNamespace + "Button"),
            element => (string?)element.Attribute("Click") == "OnToggleRollUpClick");

        Assert.Equal("{Binding HeaderRowHeight}", (string?)definitions[0].Attribute("Height"));
        Assert.Equal("{Binding ContentRowHeight}", (string?)definitions[1].Attribute("Height"));
        Assert.Equal("2", (string?)button.Attribute("Grid.Column"));
    }

    private static string GetDesktopBoxWindowXamlPath() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "WitchDrawer.App",
                "Views",
                "DesktopBoxWindow.xaml"));
}
