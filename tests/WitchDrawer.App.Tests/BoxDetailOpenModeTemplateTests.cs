using System.IO;
using System.Xml.Linq;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 验证「查看详细信息」开关卡片内的「打开方式（单击/双击）」子选项 XAML 结构：
/// 两个单选按钮绑定 SetDetailOpenModeCommand，且仅在详细功能开启后显示。
/// </summary>
public sealed class BoxDetailOpenModeTemplateTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string SetOpenModeCommandBinding =
        "{Binding SelectedBox.SetDetailOpenModeCommand}";

    [Fact]
    public void DetailOpenModeRow_HasSingleAndDoubleButtons()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        var buttons = document
            .Descendants(PresentationNamespace + "Button")
            .Where(button => (string?)button.Attribute("Command") == SetOpenModeCommandBinding)
            .ToArray();

        Assert.Equal(2, buttons.Length);
        Assert.Contains(buttons, button => (string?)button.Attribute("CommandParameter") == "Single");
        Assert.Contains(buttons, button => (string?)button.Attribute("CommandParameter") == "Double");
    }

    [Fact]
    public void DetailOpenModeRow_VisibleOnlyWhenDetailExpandEnabled()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        // 两个单选按钮的父级是打开方式子行 StackPanel。
        var modeRow = (XElement?)document
            .Descendants(PresentationNamespace + "Button")
            .First(button => (string?)button.Attribute("Command") == SetOpenModeCommandBinding
                             && (string?)button.Attribute("CommandParameter") == "Single")
            .Parent;

        Assert.NotNull(modeRow);
        Assert.Equal(
            "{Binding SelectedBox.IsDetailExpandEnabled, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)modeRow.Attribute("Visibility"));
    }

    [Fact]
    public void DetailExpandCard_SubtitleMentionsOpenMode()
    {
        var document = XDocument.Load(GetMainWindowXamlPath());
        // 开关卡片副文本提示打开方式选择（不再提"每个文件"条目级设置）。
        Assert.Contains(
            document.Descendants(PresentationNamespace + "TextBlock"),
            element => string.Equals(
                (string?)element.Attribute("Text"),
                "开启后可选单击或双击打开详细视图"));
    }

    private static string GetMainWindowXamlPath() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "WitchDrawer.App", "MainWindow.xaml"));
}
