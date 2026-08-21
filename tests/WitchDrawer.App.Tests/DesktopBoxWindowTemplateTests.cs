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
    public void IconGrid_GrowsWithAdaptiveContentWithoutScrollBars()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var iconList = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element =>
                (string?)element.Attribute(XamlNamespace + "Name") == "IconList");

        Assert.Null(iconList.Attribute("MaxWidth"));
        Assert.Null(iconList.Attribute("MaxHeight"));
        Assert.Equal(
            "Disabled",
            (string?)iconList.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Equal(
            "Disabled",
            (string?)iconList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
    }

    [Fact]
    public void SelectedIcon_UsesTheLayoutRoundedRootBorderWithoutManualOffset()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var iconList = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "IconList");
        var root = Assert.Single(
            iconList.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "Root");
        var selectedTrigger = Assert.Single(
            iconList.Descendants(PresentationNamespace + "Trigger"),
            element =>
                (string?)element.Attribute("Property") == "IsSelected"
                && (string?)element.Attribute("Value") == "True"
                && element.Elements(PresentationNamespace + "Setter").Any(
                    setter =>
                        (string?)setter.Attribute("TargetName") == "Root"
                        && (string?)setter.Attribute("Property") == "BorderBrush"));

        Assert.Equal(
            "{Binding DataContext.LayoutSettings.ItemMargin, RelativeSource={RelativeSource AncestorType={x:Type ListBox}}}",
            (string?)root.Attribute("Margin"));
        Assert.DoesNotContain(
            iconList.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "SelectionOutline");
        Assert.DoesNotContain(
            selectedTrigger.Ancestors(PresentationNamespace + "ControlTemplate")
                .Descendants(PresentationNamespace + "TranslateTransform"),
            transform =>
                (string?)transform.Attribute("X") == "-0.30"
                || (string?)transform.Attribute("Y") == "-0.30");
    }

    [Fact]
    public void DrawerSelection_UsesTheOuterLayoutRoundedFrameLikeNormalItems()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var drawerStyle = Assert.Single(
            document.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key") == "DrawerTileButtonStyle");
        var borderThickness = Assert.Single(
            drawerStyle.Elements(PresentationNamespace + "Setter"),
            setter => (string?)setter.Attribute("Property") == "BorderThickness");
        var selectedTrigger = Assert.Single(
            drawerStyle.Descendants(PresentationNamespace + "DataTrigger"),
            element =>
                (string?)element.Attribute("Binding") == "{Binding IsSelected}"
                && (string?)element.Attribute("Value") == "True");

        Assert.Equal("1.2", (string?)borderThickness.Attribute("Value"));
        Assert.Contains(
            selectedTrigger.Elements(PresentationNamespace + "Setter"),
            setter =>
                (string?)setter.Attribute("TargetName") == "DrawerButtonRoot"
                && (string?)setter.Attribute("Property") == "BorderBrush");
        Assert.DoesNotContain(
            document.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerSelectionOutline");
        Assert.DoesNotContain(
            drawerStyle.Descendants(PresentationNamespace + "TranslateTransform"),
            transform =>
                (string?)transform.Attribute("X") == "-0.30"
                || (string?)transform.Attribute("Y") == "-0.30");

        var coverItems = Assert.Single(
            document.Descendants(PresentationNamespace + "ItemsControl"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerCoverItems");
        Assert.DoesNotContain(
            coverItems.Descendants(PresentationNamespace + "Border")
                .Descendants(PresentationNamespace + "DataTrigger"),
            trigger => (string?)trigger.Attribute("Binding") == "{Binding IsSelected}");
    }

    [Fact]
    public void DrawerTemplates_ShowFileNamesAndUseTheTextAwareCellHeight()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var coverFileName = Assert.Single(
            document.Descendants(PresentationNamespace + "TextBlock"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerCoverFileName");
        var secondaryFileName = Assert.Single(
            document.Descendants(PresentationNamespace + "TextBlock"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerSecondaryFileName");
        var expandFileName = Assert.Single(
            document.Descendants(PresentationNamespace + "TextBlock"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerExpandFileName");
        var secondaryPanel = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "CenteredUniformPanel");

        Assert.Equal("{Binding Item.DisplayName}", (string?)coverFileName.Attribute("Text"));
        Assert.Equal("{Binding DisplayName}", (string?)secondaryFileName.Attribute("Text"));
        Assert.Equal("抽屉", (string?)expandFileName.Attribute("Text"));
        Assert.Equal(
            "{Binding DataContext.IsFileNameVisible, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)coverFileName.Attribute("Visibility"));
        Assert.Equal(
            "{Binding DataContext.IsFileNameVisible, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)secondaryFileName.Attribute("Visibility"));
        Assert.Equal(
            "{Binding DataContext.IsFileNameVisible, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)expandFileName.Attribute("Visibility"));
        Assert.Equal(
            "{Binding DataContext.LayoutSettings.ItemSlotHeight, RelativeSource={RelativeSource AncestorType={x:Type ListBox}}}",
            (string?)secondaryPanel.Attribute("CellHeight"));
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
