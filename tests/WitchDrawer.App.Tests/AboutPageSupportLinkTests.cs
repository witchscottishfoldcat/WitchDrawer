using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class AboutPageSupportLinkTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AboutPage_ContainsOfficialSupportLink()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var supportButton = Assert.Single(
            document.Descendants(PresentationNamespace + "Button"),
            element => (string?)element.Attribute("Click") == "OnOpenSupportLinkClicked");
        var supportCard = Assert.Single(
            document.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "AuthorSupportCard");
        var developerCard = Assert.Single(
            document.Descendants(PresentationNamespace + "Border"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "DeveloperCard");
        var acknowledgementNote = Assert.Single(
            supportCard.Descendants(PresentationNamespace + "TextBlock"),
            element => ((string?)element.Attribute("Text"))?.Contains("备注你的 ID") == true);
        var aboutScrollViewer = Assert.Single(
            supportButton.Ancestors(PresentationNamespace + "ScrollViewer"));

        Assert.Equal("前往赞助页面", (string?)supportButton.Attribute("Content"));
        Assert.Equal("https://www.witchcat.cn/zh/support", MainWindow.SupportPageUri);
        Assert.Contains(supportButton, supportCard.Descendants(PresentationNamespace + "Button"));
        Assert.DoesNotContain(supportButton, developerCard.Descendants(PresentationNamespace + "Button"));
        Assert.True(supportCard.IsBefore(developerCard), "The support card should appear above the developer card.");
        Assert.Equal("赞助时请备注你的 ID，可加入鸣谢名单。", (string?)acknowledgementNote.Attribute("Text"));
        Assert.Equal("False", (string?)aboutScrollViewer.Attribute("CanContentScroll"));
        Assert.Equal("VerticalOnly", (string?)aboutScrollViewer.Attribute("PanningMode"));
        Assert.Contains(
            aboutScrollViewer.Descendants(PresentationNamespace + "Style"),
            style => ((string?)style.Attribute("BasedOn"))?.Contains("DrawerScrollBarStyle") == true);
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
