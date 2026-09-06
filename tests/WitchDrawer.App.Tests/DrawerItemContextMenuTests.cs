using System.IO;
using System.Xml.Linq;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DrawerItemContextMenuTests
{
    [Fact]
    public void Menu_IsCompactThemeAwareAndNotTopMost()
    {
        var document = XDocument.Load(GetMenuXamlPath());
        var root = document.Root!;
        var buttons = root.Descendants(PresentationNamespace + "Button").ToArray();
        var actionStyle = Assert.Single(
            root.Descendants(PresentationNamespace + "Style"),
            element => (string?)element.Attribute(XamlNamespace + "Key") == "ContextActionButtonStyle");

        Assert.Equal("172", (string?)root.Attribute("Width"));
        Assert.Equal("False", (string?)root.Attribute("Topmost"));
        Assert.Equal("False", (string?)root.Attribute("ShowActivated"));
        Assert.Equal(4, buttons.Length);
        Assert.Contains(
            actionStyle.Elements(PresentationNamespace + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Height"
                && (string?)setter.Attribute("Value") == "28");
        Assert.Contains(
            root.Descendants().Attributes("Background"),
            attribute => attribute.Value == "{DynamicResource DrawerSecondarySurfaceBrush}");
        Assert.Contains(
            root.Descendants().Attributes(),
            attribute => attribute.Value == "{DynamicResource AccentSoftBrush}");
    }

    [Theory]
    [InlineData(90, 90, 30, 30, 0, 0, 100, 100, 70, 70)]
    [InlineData(-20, -10, 30, 30, 0, 0, 100, 100, 0, 0)]
    [InlineData(20, 30, 30, 30, 0, 0, 100, 100, 20, 30)]
    public void MenuPosition_IsClampedToTheMonitorWorkArea(
        int x,
        int y,
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom,
        int expectedX,
        int expectedY)
    {
        Assert.Equal(
            (expectedX, expectedY),
            TransientMenuWindow.ClampToWorkArea(
                x,
                y,
                width,
                height,
                left,
                top,
                right,
                bottom));
    }

    [Theory]
    [InlineData(0, 0, 100, 100, 50, 50, false)]
    [InlineData(0, 0, 100, 100, 100, 50, true)]
    [InlineData(0, 0, 100, 100, -1, 50, true)]
    public void OutsideClickBoundary_UsesHalfOpenWindowBounds(
        int left,
        int top,
        int right,
        int bottom,
        int x,
        int y,
        bool expected)
    {
        Assert.Equal(
            expected,
            TransientMenuWindow.IsOutside(left, top, right, bottom, x, y));
    }

    [Theory]
    [InlineData(0x1B, false, true)]
    [InlineData(0x44, true, true)]
    [InlineData(0x44, false, false)]
    [InlineData(0x41, true, false)]
    public void KeyboardDismissal_IsLimitedToEscapeAndWinD(
        uint virtualKey,
        bool windowsKeyDown,
        bool expected)
    {
        Assert.Equal(
            expected,
            TransientMenuWindow.ShouldDismissForKey(virtualKey, windowsKeyDown));
    }

    private static string GetMenuXamlPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WitchDrawer.App",
            "Features",
            "ItemContextMenu",
            "DrawerItemContextMenuWindow.xaml");
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
}
