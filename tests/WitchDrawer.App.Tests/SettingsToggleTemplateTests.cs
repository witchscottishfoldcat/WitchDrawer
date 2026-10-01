using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class SettingsToggleTemplateTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SettingsBinaryActions_UseBoundToggleButtons()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var styles = XDocument.Load(GetSettingsCardStylesXamlPath());
        var style = Assert.Single(
            styles.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key") == "SettingsToggleButtonStyle");
        var toggles = document.Descendants(PresentationNamespace + "ToggleButton").ToArray();

        Assert.Equal("{x:Type ToggleButton}", (string?)style.Attribute("TargetType"));
        Assert.Contains(toggles, element =>
            (string?)element.Attribute("IsChecked") == "{Binding Settings.LaunchOnStartup, Mode=OneWay}" &&
            (string?)element.Attribute("Command") == "{Binding Settings.ToggleLaunchOnStartupCommand}");
        Assert.Contains(toggles, element =>
            (string?)element.Attribute("IsChecked") == "{Binding Settings.AreDesktopIconsHidden, Mode=OneWay}" &&
            (string?)element.Attribute("Command") == "{Binding Settings.ToggleDesktopIconsCommand}");
        Assert.Contains(toggles, element =>
            (string?)element.Attribute("IsChecked") == "{Binding Settings.IsDesktopDoubleClickEnabled, Mode=OneWay}" &&
            (string?)element.Attribute("Command") == "{Binding Settings.ToggleDesktopDoubleClickCommand}");
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

    private static string GetSettingsCardStylesXamlPath() =>
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
                "MainWindow",
                "SettingsCardStyles.xaml"));
}
