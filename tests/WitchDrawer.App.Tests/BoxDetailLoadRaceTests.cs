using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

/// <summary>
/// F2 回归测试：fire-and-forget 加载与用户切换的竞态。
/// 核心断言：加载返回的旧值绝不允许覆盖用户已切换的新状态（最后写入者胜）。
/// 通过 internal 读取委托重载注入 TaskCompletionSource 控制的读取时序，确定性复现「旧值晚到」。
/// </summary>
public sealed class BoxDetailLoadRaceTests
{
    [Fact]
    public async Task StaleExpandLoad_DoesNotOverwriteUserToggleOn()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            // 磁盘旧值：详细功能关闭。
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id),
                "False");
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            // 等待构造时 fire-and-forget 加载完成，避免干扰。
            await Task.Delay(200);
            Assert.False(viewModel.IsDetailExpandEnabled);

            // 模拟一次晚到的加载：读取挂起，期间用户打开开关。
            var gate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadTask = viewModel.LoadDetailExpandAsync(() => gate.Task);
            await viewModel.ToggleDetailExpandCommand.ExecuteAsync(null);
            Assert.True(viewModel.IsDetailExpandEnabled);

            // 旧值（False）晚到——不得覆盖用户已切换的 True。
            gate.SetResult("False");
            await loadTask;

            Assert.True(viewModel.IsDetailExpandEnabled);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task StaleExpandLoad_DoesNotOverwriteUserToggleOff()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            // 磁盘旧值：详细功能开启。
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id),
                "True");
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            await Task.Delay(200);
            Assert.True(viewModel.IsDetailExpandEnabled);

            var gate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadTask = viewModel.LoadDetailExpandAsync(() => gate.Task);
            await viewModel.ToggleDetailExpandCommand.ExecuteAsync(null);
            Assert.False(viewModel.IsDetailExpandEnabled);

            // 旧值（True）晚到——不得覆盖用户已关闭的 False。
            gate.SetResult("True");
            await loadTask;

            Assert.False(viewModel.IsDetailExpandEnabled);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task StaleOpenModeLoad_DoesNotOverwriteUserSwitch()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            // 磁盘旧值：双击展开。
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailOpenModeSettingKey(box.Id),
                "Double");
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            await Task.Delay(200);
            Assert.False(viewModel.IsDetailOpenSingle);

            var gate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadTask = viewModel.LoadDetailOpenModeAsync(() => gate.Task);
            await viewModel.SetDetailOpenModeCommand.ExecuteAsync("Single");
            Assert.True(viewModel.IsDetailOpenSingle);

            // 旧值（Double）晚到——不得覆盖用户已切换的单击展开。
            gate.SetResult("Double");
            await loadTask;

            Assert.True(viewModel.IsDetailOpenSingle);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ExpandLoad_AppliesPersistedValue_WhenNoUserInterference()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id),
                "True");
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);

            // 无竞态：加载正常生效。
            await Task.Delay(200);

            Assert.True(viewModel.IsDetailExpandEnabled);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task NonMappingBox_SkipsDetailLoads()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = new Box(
                Guid.NewGuid(),
                "普通收纳盒",
                BoxType.Normal,
                Path.Combine(root, "box"),
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            var viewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);

            // 非映射盒不应执行读取委托（委托若被调用会抛异常）。
            await viewModel.LoadDetailExpandAsync(() => throw new InvalidOperationException());
            await viewModel.LoadDetailOpenModeAsync(() => throw new InvalidOperationException());

            Assert.False(viewModel.IsDetailExpandEnabled);
            Assert.True(viewModel.IsDetailOpenSingle);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task Desktop_StaleExpandLoad_DoesNotOverwriteMessageSync()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailExpandSettingKey(box.Id),
                "False");
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);

            var gate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadTask = viewModel.LoadDetailExpandAsync(() => gate.Task);

            // 模拟主控台切换（消息同步，与加载并发）。
            WeakReferenceMessenger.Default.Send(new BoxDetailExpandChangedMessage(box.Id, true));
            Assert.True(viewModel.IsDetailExpandEnabled);

            // 旧值（False）晚到——不得覆盖消息同步的 True。
            gate.SetResult("False");
            await loadTask;

            Assert.True(viewModel.IsDetailExpandEnabled);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task Desktop_StaleOpenModeLoad_DoesNotOverwriteMessageSync()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = CreateMappingBox();
            await drawerService.SetSettingAsync(
                BoxViewModel.GetDetailOpenModeSettingKey(box.Id),
                "Double");
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);

            var gate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loadTask = viewModel.LoadDetailOpenModeAsync(() => gate.Task);

            // 模拟主控台切换为双击展开（与默认单击相反，状态确实改变）。
            WeakReferenceMessenger.Default.Send(new BoxDetailOpenModeChangedMessage(box.Id, false));
            Assert.False(viewModel.IsDetailOpenSingle);

            // 旧值（Double）晚到——不得覆盖消息同步的双击展开。
            gate.SetResult("Double");
            await loadTask;

            Assert.False(viewModel.IsDetailOpenSingle);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

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
