using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class ThemeOpacityControlTemplateTests
{
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
