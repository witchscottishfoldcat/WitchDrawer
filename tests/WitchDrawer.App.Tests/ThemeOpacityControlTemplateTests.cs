using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class ThemeOpacityControlTemplateTests
{
    [Theory]
    [InlineData("BoxBorderTransparency", "盒子边线透明度")]
    [InlineData("IconFrameTransparency", "图标背景框透明度")]
    public void AppearanceControls_HaveIndependentBindingsAndAllowFullTransparency(string prefix, string label)
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var slider = Assert.Single(document.Descendants(PresentationNamespace + "Slider"),
            element => ((string?)element.Attribute("Value"))?.Contains(prefix + "Percent") == true);
        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal("100", (string?)slider.Attribute("Maximum"));
        Assert.Equal("True", (string?)slider.Attribute("IsMoveToPointEnabled"));
        Assert.Equal(label, (string?)slider.Attribute("AutomationProperties.Name"));
        var input = Assert.Single(document.Descendants(PresentationNamespace + "TextBox"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == prefix + "Input");
        Assert.Contains(prefix + "Percent", (string?)input.Attribute("Text"));
        Assert.Equal("OnThemeTransparencyInputKeyDown", (string?)input.Attribute("KeyDown"));
        Assert.Equal("OnThemeTransparencyInputLostFocus", (string?)input.Attribute("LostFocus"));
    }

    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TransparencySlider_ClickMovesDirectlyToPointer()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var slider = Assert.Single(
            document.Descendants(PresentationNamespace + "Slider"),
            element => ((string?)element.Attribute("Value"))?.Contains("ThemeTransparencyPercent") == true);

        Assert.Equal("True", (string?)slider.Attribute("IsMoveToPointEnabled"));
    }

    [Fact]
    public void TransparencyPercent_IsDirectlyEditable()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var input = Assert.Single(
            document.Descendants(PresentationNamespace + "TextBox"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "ThemeTransparencyInput");

        Assert.Contains("ThemeTransparencyPercent", (string?)input.Attribute("Text"));
        Assert.Equal("OnThemeTransparencyInputKeyDown", (string?)input.Attribute("KeyDown"));
        Assert.Equal("OnThemeTransparencyInputLostFocus", (string?)input.Attribute("LostFocus"));
    }

    [Fact]
    public void EditorOpacityFollow_IsExposedBesideTheTransparencySlider()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var toggle = Assert.Single(
            document.Descendants(PresentationNamespace + "ToggleButton"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "EditorOpacityFollowToggle");

        Assert.Equal(
            "{Binding EditorFollowsBoxOpacity, Mode=OneWay}",
            (string?)toggle.Attribute("IsChecked"));
        Assert.Equal(
            "{Binding ToggleEditorOpacityFollowCommand}",
            (string?)toggle.Attribute("Command"));
        Assert.Equal(
            "编辑页跟随透明度",
            (string?)toggle.Attribute("AutomationProperties.Name"));
    }

    private static string GetMainWindowXamlPath() =>
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
                "MainWindow.xaml"));
}
