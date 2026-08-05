using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxViewModelLayoutTests
{
    [Fact]
    public async Task ApplyMaxRows_LimitsGridCanvasHeight()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.CreateBoxAsync(BoxType.Normal);
        var viewModel = await workspace.CreateViewModelAsync(box);
        await viewModel.LoadAsync();

        // 3 列预设：10 项自动放置为 3+3+3+1 = 4 行。
        viewModel.LayoutSettings.ApplyPresetWithoutCallback("3x3");
        for (var index = 0; index < 10; index++)
        {
            var file = workspace.CreateSourceFile("rows", $"f{index}.txt", "x");
            await viewModel.ImportPathsAsync([file]);
        }

        var slotHeight = viewModel.LayoutSettings.ItemSlotHeight;
        Assert.True(viewModel.GridCanvasHeight > slotHeight * 3, "导入 10 项后画布应超过 3 行高。");

        viewModel.ApplyMaxRows(2);

        Assert.Equal(2, viewModel.MaxRows);
        Assert.Equal(slotHeight * 2, viewModel.GridCanvasHeight, precision: 5);
        Assert.Equal(slotHeight * 2, viewModel.MaxGridCanvasHeight, precision: 5);

        viewModel.ApplyMaxRows(null);
        Assert.Null(viewModel.MaxRows);
        Assert.True(viewModel.GridCanvasHeight > slotHeight * 3, "取消行数限制后画布恢复完整高度。");
    }

    [Fact]
    public async Task ApplyMaxRows_PersistsAndRestores()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.CreateBoxAsync(BoxType.Normal);
        var viewModel = await workspace.CreateViewModelAsync(box);

        await viewModel.SaveMaxRowsAsync(3);
        await viewModel.LoadMaxRowsAsync();

        Assert.Equal(3, viewModel.MaxRows);
        Assert.Equal(
            "3",
            await workspace.Service.GetSettingAsync(DesktopBoxViewModel.GetMaxRowsSettingKey(box.Id)));
    }

    [Fact]
    public async Task ItemNameVisibility_DefaultsVisibleAndPersists()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.CreateBoxAsync(BoxType.Normal);
        var viewModel = await workspace.CreateViewModelAsync(box);
        await viewModel.LoadItemNameVisibilityAsync();

        Assert.True(viewModel.IsItemNameVisible);

        viewModel.ApplyItemNameVisibility(false);
        await workspace.Service.SetSettingAsync(
            DesktopBoxViewModel.GetItemNameVisibilitySettingKey(box.Id),
            bool.FalseString);

        var reloaded = await workspace.CreateViewModelAsync(box);
        await reloaded.LoadItemNameVisibilityAsync();
        Assert.False(reloaded.IsItemNameVisible);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, AppPaths paths, DrawerService service)
        {
            Root = root;
            Paths = paths;
            Service = service;
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public DrawerService Service { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            return new TestWorkspace(root, paths, service);
        }

        public async Task<Box> CreateBoxAsync(BoxType type)
        {
            return await Service.CreateBoxAsync($"box-{Guid.NewGuid():N}"[..8], type);
        }

        public string CreateSourceFile(string folderName, string fileName, string content)
        {
            var directory = Path.Combine(Root, "sources", folderName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public async Task<DesktopBoxViewModel> CreateViewModelAsync(Box box)
        {
            var repository = new DrawerRepository(Paths.DatabasePath);
            return new DesktopBoxViewModel(
                box,
                Service,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OpenAsAdminAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ShowInFolderAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
