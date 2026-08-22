using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using WitchDrawer.App.Features.DesktopItems;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxWindowTemplateTests
{
    [Theory]
    [InlineData(MouseButton.Left, true)]
    [InlineData(MouseButton.Right, false)]
    [InlineData(MouseButton.Middle, false)]
    public void ItemDoubleClick_OnlyOpensWithTheLeftButton(
        MouseButton changedButton,
        bool expected)
    {
        Assert.Equal(expected, DesktopItemInputRules.ShouldOpenOnDoubleClick(changedButton));
    }

    [Theory]
    [InlineData("program.exe", true)]
    [InlineData("shortcut.lnk", true)]
    [InlineData("script.cmd", true)]
    [InlineData("document.txt", false)]
    public void AdministratorAction_IsLimitedToExecutableFileTypes(string path, bool expected)
    {
        Assert.Equal(
            expected,
            WindowsFileShellActions.CanRunAsAdministrator(path, isDirectory: false));
    }

    [Fact]
    public void ItemContextMenu_IsWiredWithoutChangingItemContainerTemplates()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var handlers = document
            .Descendants()
            .Attributes("PreviewMouseRightButtonUp")
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Equal(4, handlers.Length);
        Assert.Equal(2, handlers.Count(value => value == "OnIconPreviewMouseRightButtonUp"));
        Assert.Contains("OnDrawerSecondaryIconMouseRightButtonUp", handlers);
        Assert.Contains("OnDrawerCoverIconMouseRightButtonUp", handlers);

        var iconList = Assert.Single(
            document.Descendants(PresentationNamespace + "ListBox"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "IconList");
        Assert.Equal(
            "{StaticResource DesktopIconListItemStyle}",
            (string?)iconList.Attribute("ItemContainerStyle"));
        Assert.Empty(iconList.Elements(PresentationNamespace + "ListBox.ItemContainerStyle"));
    }

    [Fact]
    public void SelectionStyles_AreLoadedFromAnIsolatedResourceDictionary()
    {
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var sources =
            document.Descendants(PresentationNamespace + "ResourceDictionary")
                .Attributes("Source")
                .Select(attribute => attribute.Value)
                .ToArray();

        Assert.Contains(
            "/WitchDrawer.App;component/Views/Styles/DesktopBoxSelectionStyles.xaml",
            sources);
        Assert.Contains(
            "/WitchDrawer.App;component/Views/Styles/DesktopBoxControlStyles.xaml",
            sources);
    }

    private static Thickness ParseThickness(string? value) =>
        (Thickness)new ThicknessConverter().ConvertFromInvariantString(value ?? "0")!;

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
    public void SelectedIcon_UsesAnOuterOutlineWithoutRecoloringTheIconBorder()
    {
        var document = XDocument.Load(GetDesktopBoxSelectionStylesXamlPath());
        var itemStyle = Assert.Single(
            document.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key") == "DesktopIconListItemStyle");
        var outline = Assert.Single(
            itemStyle.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "SelectionOutline");
        var selectedTrigger = Assert.Single(
            itemStyle.Descendants(PresentationNamespace + "Trigger"),
            element =>
                (string?)element.Attribute("Property") == "IsSelected"
                && (string?)element.Attribute("Value") == "True"
                && element.Elements(PresentationNamespace + "Setter").Any(
                    setter => (string?)setter.Attribute("TargetName") == "SelectionOutline"));
        var transform = Assert.Single(
            outline.Descendants(PresentationNamespace + "TranslateTransform"));

        Assert.Equal("1.2", (string?)outline.Attribute("BorderThickness"));
        Assert.Equal("False", (string?)outline.Attribute("SnapsToDevicePixels"));
        Assert.Equal("-0.30", (string?)transform.Attribute("X"));
        Assert.Equal("-0.30", (string?)transform.Attribute("Y"));
        Assert.Contains(
            selectedTrigger.Elements(PresentationNamespace + "Setter"),
            setter =>
                (string?)setter.Attribute("TargetName") == "SelectionOutline"
                && (string?)setter.Attribute("Property") == "BorderBrush");
    }

    [Fact]
    public void DrawerSelection_UsesTheSameOuterOutlineAsTheIconGrid()
    {
        var document = XDocument.Load(GetDesktopBoxSelectionStylesXamlPath());
        var drawerStyle = Assert.Single(
            document.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key") == "DrawerTileButtonStyle");
        var outline = Assert.Single(
            drawerStyle.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerSelectionOutline");
        var selectedTrigger = Assert.Single(
            drawerStyle.Descendants(PresentationNamespace + "DataTrigger"),
            element =>
                (string?)element.Attribute("Binding") == "{Binding IsSelected}"
                && (string?)element.Attribute("Value") == "True"
                && element.Elements(PresentationNamespace + "Setter").Any(
                    setter => (string?)setter.Attribute("TargetName") == "DrawerSelectionOutline"));
        var transform = Assert.Single(
            outline.Descendants(PresentationNamespace + "TranslateTransform"));

        Assert.Equal("1.2", (string?)outline.Attribute("BorderThickness"));
        Assert.Equal("False", (string?)outline.Attribute("SnapsToDevicePixels"));
        Assert.Equal("-0.30", (string?)transform.Attribute("X"));
        Assert.Equal("-0.30", (string?)transform.Attribute("Y"));
        Assert.Contains(
            selectedTrigger.Elements(PresentationNamespace + "Setter"),
            setter =>
                (string?)setter.Attribute("TargetName") == "DrawerSelectionOutline"
                && (string?)setter.Attribute("Property") == "BorderBrush");

        var windowDocument = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var coverItems = Assert.Single(
            windowDocument.Descendants(PresentationNamespace + "ItemsControl"),
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
    public void DrawerSecondaryPopup_ChromeReserveMatchesXamlBorderAndListMargin()
    {
        // DesktopBoxViewModel.DrawerSecondaryPanelChrome 必须与弹窗 XAML 中的
        // 根 Border 描边 + ListBox Margin + ListBox 默认(Aero2)模板内 Border 的
        // Padding (1px × 2，硬编码在主题模板中) 一一对应，否则内容区会比可视口
        // 大出几像素，最下列图标会被弹窗下边缘裁掉。
        const double listBoxTemplateBorderPadding = 2;
        var document = XDocument.Load(GetDesktopBoxWindowXamlPath());
        var popupRoot = Assert.Single(
            document.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DrawerSecondaryPopupRoot");
        var listBox = Assert.Single(popupRoot.Descendants(PresentationNamespace + "ListBox"));

        var borderThickness = ParseThickness((string?)popupRoot.Attribute("BorderThickness"));
        var listMargin = ParseThickness((string?)listBox.Attribute("Margin"));

        Assert.Equal(
            DesktopBoxViewModel.DrawerSecondaryPanelChrome,
            borderThickness.Left + borderThickness.Right + listMargin.Left + listMargin.Right
                + listBoxTemplateBorderPadding);
        Assert.Equal(
            DesktopBoxViewModel.DrawerSecondaryPanelChrome,
            borderThickness.Top + borderThickness.Bottom + listMargin.Top + listMargin.Bottom
                + listBoxTemplateBorderPadding);
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

    private static string GetDesktopBoxSelectionStylesXamlPath() =>
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
                "Styles",
                "DesktopBoxSelectionStyles.xaml"));
}
