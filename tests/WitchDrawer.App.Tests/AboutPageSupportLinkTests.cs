using System.IO;
using System.Xml.Linq;
using WitchDrawer.App.Views;

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
        var document = XDocument.Load(GetAboutPageXamlPath());
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
            element => (string?)element.Attribute("Text") == "{loc:Text IncludeYourIDWhenSponsoringToBeAddedTo}");
        var aboutScrollViewer = Assert.Single(
            supportButton.Ancestors(PresentationNamespace + "ScrollViewer"));

        Assert.Equal("{loc:Text VisitSponsorshipPage}", (string?)supportButton.Attribute("Content"));
        Assert.Equal("https://www.witchcat.cn/zh/support", AboutPageView.SupportPageUri);
        Assert.Contains(supportButton, supportCard.Descendants(PresentationNamespace + "Button"));
        Assert.DoesNotContain(supportButton, developerCard.Descendants(PresentationNamespace + "Button"));
        Assert.True(supportCard.IsBefore(developerCard), "The support card should appear above the developer card.");
        Assert.Equal("{loc:Text IncludeYourIDWhenSponsoringToBeAddedTo}", (string?)acknowledgementNote.Attribute("Text"));
        Assert.Equal("False", (string?)aboutScrollViewer.Attribute("CanContentScroll"));
        Assert.Equal("VerticalOnly", (string?)aboutScrollViewer.Attribute("PanningMode"));
        Assert.Contains(
            aboutScrollViewer.Descendants(PresentationNamespace + "Style"),
            style => ((string?)style.Attribute("BasedOn"))?.Contains("DrawerScrollBarStyle") == true);
    }

    [Fact]
    public void AboutPage_ContainsDiagnosticLogExportWithPrivacyNotice()
    {
        var document = XDocument.Load(GetAboutPageXamlPath());
        var exportButton = Assert.Single(
            document.Descendants(PresentationNamespace + "Button"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "ExportDiagnosticLogsButton");
        var privacyNotice = Assert.Single(
            document.Descendants(PresentationNamespace + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{loc:Text ExcludesTheDatabaseAndUserFilesLogsMayInclude}");

        Assert.Equal("{loc:Text ExportDiagnosticLogs}", (string?)exportButton.Attribute("Content"));
        Assert.Equal("OnExportDiagnosticLogsClick", (string?)exportButton.Attribute("Click"));
        Assert.NotEmpty(exportButton.Ancestors(PresentationNamespace + "ScrollViewer"));
        Assert.NotEmpty(privacyNotice.Ancestors(PresentationNamespace + "ScrollViewer"));
    }

    private static string GetAboutPageXamlPath() =>
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
                "AboutPageView.xaml"));
}
