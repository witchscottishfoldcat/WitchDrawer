using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

public sealed class BoxActionMenuTemplateTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PositionActions_ArePlacedInTheirIntendedScopes()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var actionsPopup = Assert.Single(
            document.Descendants(PresentationNamespace + "Popup"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "BoxActionsPopup");
        var actionButtons = actionsPopup.Descendants(PresentationNamespace + "Button").ToArray();
        var recallPopup = Assert.Single(
            document.Descendants(PresentationNamespace + "Popup"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "RecallBoxConfirmPopup");

        Assert.DoesNotContain(
            actionButtons,
            button => (string?)button.Attribute("Click") == "OnSaveBoxPositionClicked");
        Assert.DoesNotContain(
            actionButtons,
            button => (string?)button.Attribute("Click") == "OnRecordLayoutBackupClicked");
        Assert.Contains(
            actionButtons,
            button => (string?)button.Attribute("Click") == "OnRecallBoxClicked");
        var recordButtons = document
            .Descendants(PresentationNamespace + "Button")
            .Where(button => (string?)button.Attribute("Click") == "OnRecordLayoutBackupClicked")
            .ToArray();
        var restoreButtons = document
            .Descendants(PresentationNamespace + "Button")
            .Where(button => (string?)button.Attribute("Click") == "OnRestoreLayoutBackupClicked")
            .ToArray();
        var deleteButtons = document
            .Descendants(PresentationNamespace + "Button")
            .Where(button => (string?)button.Attribute("Click") == "OnDeleteLayoutBackupClicked")
            .ToArray();

        Assert.Equal(["1", "2", "3"], recordButtons.Select(button => (string?)button.Attribute("Tag")));
        Assert.Equal(["1", "2", "3"], restoreButtons.Select(button => (string?)button.Attribute("Tag")));
        Assert.Equal(["1", "2", "3"], deleteButtons.Select(button => (string?)button.Attribute("Tag")));
        Assert.All(
            restoreButtons,
            button => Assert.Equal("Collapsed", (string?)button.Attribute("Visibility")));
        Assert.All(
            deleteButtons,
            button => Assert.Equal("Collapsed", (string?)button.Attribute("Visibility")));
        for (var slot = 1; slot <= 3; slot++)
        {
            var status = Assert.Single(
                document.Descendants(PresentationNamespace + "TextBlock"),
                element => (string?)element.Attribute(XamlNamespace + "Name") == $"LayoutBackupSlot{slot}Status");
            Assert.Equal("未记录", (string?)status.Attribute("Text"));
        }
        Assert.Contains(
            recallPopup.Descendants(PresentationNamespace + "Button"),
            button => (string?)button.Attribute("Click") == "OnConfirmRecallBoxClicked");
        Assert.Contains(
            recallPopup.Descendants(PresentationNamespace + "Button"),
            button => (string?)button.Attribute("Click") == "OnCancelRecallBoxClicked");
    }

    [Fact]
    public void RecallConfirmation_BoxNameBindingIsOneWay()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var recallPopup = Assert.Single(
            document.Descendants(PresentationNamespace + "Popup"),
            element => (string?)element.Attribute(XamlNamespace + "Name") == "RecallBoxConfirmPopup");
        var nameRun = Assert.Single(
            recallPopup.Descendants(PresentationNamespace + "Run"),
            element => ((string?)element.Attribute("Text"))?.Contains("SelectedBox.Name") == true);

        Assert.Contains("Mode=OneWay", (string?)nameRun.Attribute("Text"));
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
