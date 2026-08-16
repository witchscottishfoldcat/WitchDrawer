using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 「详细视图打开方式」交互相关测试：
/// ① 展开决策纯函数（双击模式下单击不得展开 —— 用户人工测试回归）；
/// ② 主控台切换打开方式 → 桌面盒消息同步链路；
/// ③ 切换命令正常路径（Q1 修复后的行为）。
/// </summary>
public sealed class DetailOpenModeInteractionTests
{
    // ---- ① 展开决策纯函数（ShouldExpandDetailView 全组合） ----

    [Theory]
    [InlineData(false, true, 1, false)]   // 双击模式 + 单击 → 不得展开（用户回归）
    [InlineData(false, true, 2, true)]    // 双击模式 + 双击 → 展开
    [InlineData(false, true, 3, true)]    // 双击模式 + 多击（第 3 次 MouseUp 冒泡）→ 已展开态由 isExpanded 兜底
    [InlineData(true, false, 1, true)]    // 单击模式 + 单击 → 展开
    [InlineData(true, false, 2, false)]   // 单击模式 + 双击 → 不重复展开
    public void ShouldExpandDetailView_OpenModeAndClickCount(
        bool isClickToOpen,
        bool isDoubleClickToOpen,
        int clickCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxWindow.ShouldExpandDetailView(
                isClickToOpen,
                isDoubleClickToOpen,
                clickCount,
                isListMode: false,
                isExpanded: false,
                isAnimating: false));
    }

    [Theory]
    [InlineData(true, false, 1)]   // 单击模式 + 单击
    [InlineData(false, true, 2)]   // 双击模式 + 双击
    public void ShouldExpandDetailView_GuardStates_NeverExpand(
        bool isClickToOpen,
        bool isDoubleClickToOpen,
        int clickCount)
    {
        Assert.False(DesktopBoxWindow.ShouldExpandDetailView(
            isClickToOpen,
            isDoubleClickToOpen,
            clickCount,
            isListMode: true,
            isExpanded: false,
            isAnimating: false));
        Assert.False(DesktopBoxWindow.ShouldExpandDetailView(
            isClickToOpen,
            isDoubleClickToOpen,
            clickCount,
            isListMode: false,
            isExpanded: true,
            isAnimating: false));
        Assert.False(DesktopBoxWindow.ShouldExpandDetailView(
            isClickToOpen,
            isDoubleClickToOpen,
            clickCount,
            isListMode: false,
            isExpanded: false,
            isAnimating: true));
    }

    [Fact]
    public void ShouldExpandDetailView_InvalidClickCount_NeverExpands()
    {
        Assert.False(DesktopBoxWindow.ShouldExpandDetailView(
            isClickToOpen: true,
            isDoubleClickToOpen: false,
            clickCount: 0,
            isListMode: false,
            isExpanded: false,
            isAnimating: false));
    }
    // ---- ② 主控台切换 → 桌面盒消息同步链路 ----

    [Fact]
    public async Task MainConsole_SwitchToDoubleClick_SyncsToDesktopBox()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();

            // 主控台 VM 与桌面盒 VM 是同一盒的两个独立实例（真实架构）。
            var consoleVm = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            var desktopVm = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);
            await Task.Delay(200); // 等待主控台构造期 fire-and-forget 加载完成

            // 用户先开启「详细功能」，再在主控台切到「双击」（真实操作顺序）。
            await consoleVm.ToggleDetailExpandCommand.ExecuteAsync(null);
            Assert.True(desktopVm.IsDetailExpandEnabled);

            await consoleVm.SetDetailOpenModeCommand.ExecuteAsync("Double");

            // 桌面盒 VM 应通过消息同步为双击模式：单击不得展开、双击展开。
            Assert.False(desktopVm.IsDetailOpenSingle);
            Assert.False(desktopVm.IsDetailClickToOpen);
            Assert.True(desktopVm.IsDetailDoubleClickToOpen);
            Assert.Equal("Double", await drawerService.GetSettingAsync(
                BoxViewModel.GetDetailOpenModeSettingKey(box.Id)));

            // 切回「单击」同样即时同步。
            await consoleVm.SetDetailOpenModeCommand.ExecuteAsync("Single");
            Assert.True(desktopVm.IsDetailOpenSingle);
            Assert.True(desktopVm.IsDetailClickToOpen);
            Assert.False(desktopVm.IsDetailDoubleClickToOpen);
            Assert.Equal("Single", await drawerService.GetSettingAsync(
                BoxViewModel.GetDetailOpenModeSettingKey(box.Id)));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task MainConsole_ToggleDetailExpand_SyncsToDesktopBox()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            var consoleVm = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            var desktopVm = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);
            await Task.Delay(200);

            await consoleVm.ToggleDetailExpandCommand.ExecuteAsync(null);
            Assert.True(consoleVm.IsDetailExpandEnabled);
            Assert.True(desktopVm.IsDetailExpandEnabled);
            Assert.Equal("True", await drawerService.GetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id)));

            await consoleVm.ToggleDetailExpandCommand.ExecuteAsync(null);
            Assert.False(consoleVm.IsDetailExpandEnabled);
            Assert.False(desktopVm.IsDetailExpandEnabled);
            Assert.Equal("False", await drawerService.GetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id)));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    // ---- ③ Q1 修复后的命令正常路径 ----

    [Fact]
    public async Task SetDetailOpenMode_SameMode_IsNoOp()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            await Task.Delay(200);

            // 默认已是单击：重复设置同值应早退（不写盘、不发消息）。
            await viewModel.SetDetailOpenModeCommand.ExecuteAsync("Single");

            Assert.True(viewModel.IsDetailOpenSingle);
            Assert.Null(await drawerService.GetSettingAsync(
                BoxViewModel.GetDetailOpenModeSettingKey(box.Id)));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    // ---- helpers ----

    private static Box CreateMappingBox() =>
        new(
            Guid.NewGuid(),
            "映射收纳盒",
            BoxType.Mapping,
            null,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateDrawerServiceAsync(
        string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var drawerService = new DrawerService(paths, repository);
        await drawerService.InitializeAsync();
        return (drawerService, repository);
    }

    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
