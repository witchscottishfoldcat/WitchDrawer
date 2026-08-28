using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定“图标名称”悬停提示的显示模式：完整模式显示文件完整路径；
/// 精简模式仅显示文件名，快捷方式（.lnk）自动去掉扩展名。
/// </summary>
public sealed class IconToolTipModeTests
{
    private const string RegularPath = @"C:\Users\Test\Documents\report.pdf";
    private const string ShortcutPath = @"C:\Users\Test\Desktop\MyApp.lnk";

    private static DrawerItemViewModel Item(string path)
    {
        var model = new DrawerItem(
            Id: Guid.NewGuid(),
            BoxId: Guid.NewGuid(),
            DisplayName: path,
            ItemKind: ItemKind.File,
            SourcePath: path,
            StoredPath: null,
            SortOrder: 0,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
        return new DrawerItemViewModel(model);
    }

    [Fact]
    public void HoverDisplayText_FullMode_ReturnsFullPath()
    {
        DesktopHoverDisplayMode.IsCompact = false;
        try
        {
            Assert.Equal(RegularPath, Item(RegularPath).HoverDisplayText);
        }
        finally
        {
            DesktopHoverDisplayMode.IsCompact = false;
        }
    }

    [Fact]
    public void HoverDisplayText_CompactMode_ReturnsFileName()
    {
        DesktopHoverDisplayMode.IsCompact = true;
        try
        {
            Assert.Equal("report.pdf", Item(RegularPath).HoverDisplayText);
        }
        finally
        {
            DesktopHoverDisplayMode.IsCompact = false;
        }
    }

    [Fact]
    public void HoverDisplayText_CompactMode_RemovesLnkExtension()
    {
        DesktopHoverDisplayMode.IsCompact = true;
        try
        {
            Assert.Equal("MyApp", Item(ShortcutPath).HoverDisplayText);
        }
        finally
        {
            DesktopHoverDisplayMode.IsCompact = false;
        }
    }

    [Fact]
    public void HoverDisplayText_CompactMode_KeepsOtherExtensions()
    {
        DesktopHoverDisplayMode.IsCompact = true;
        try
        {
            Assert.Equal("archive.tar.gz", Item(@"C:\data\archive.tar.gz").HoverDisplayText);
        }
        finally
        {
            DesktopHoverDisplayMode.IsCompact = false;
        }
    }
}