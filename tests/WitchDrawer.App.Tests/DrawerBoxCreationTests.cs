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

public sealed class DrawerBoxCreationTests
{
    [Fact]
    public async Task CreateDrawerBoxCommand_CreatesDrawerWithDefaultLayoutAndVisibleTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var viewModel = new MainViewModel(
                drawerService,
                new TodoService(repository),
                launcher,
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));
            var existingIds = (await drawerService.GetBoxesAsync()).Select(box => box.Id).ToHashSet();

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);

            var createdBox = Assert.Single(
                await drawerService.GetBoxesAsync(),
                box => !existingIds.Contains(box.Id));
            Assert.Equal(BoxType.Drawer, createdBox.Type);
            Assert.Equal(
                DesktopBoxLayoutSettings.DefaultDrawerPreset,
                await drawerService.GetSettingAsync(BoxViewModel.GetLayoutPresetSettingKey(createdBox.Id)));
            Assert.True(viewModel.SelectedBox?.IsDrawerBox);
            var selectedDrawer = Assert.IsType<BoxViewModel>(viewModel.SelectedBox);
            Assert.False(selectedDrawer.SupportsFileNameVisibility);
            await selectedDrawer.LoadTitleVisibilityAsync();
            Assert.True(selectedDrawer.IsTitleVisible);
            Assert.Equal(
                BoxViewModel.GetTitleVisibilitySettingKey(createdBox.Id),
                DesktopBoxViewModel.GetTitleVisibilitySettingKey(createdBox.Id));
            await selectedDrawer.LoadDrawerSortModeAsync();
            // 统一排序：所有盒型默认自由排序（抽屉旧设置值自动迁移）。
            Assert.Equal(DrawerItemSortMode.Free, selectedDrawer.DrawerItemSortMode);
            Assert.True(selectedDrawer.IsFreeSort);
            Assert.Equal("自由", selectedDrawer.DrawerSortModeLabel);
            Assert.Equal(
                BoxViewModel.GetBoxSortModeSettingKey(createdBox.Id),
                DesktopBoxViewModel.GetBoxSortModeSettingKey(createdBox.Id));

            await selectedDrawer.ApplyDrawerSortModeCommand.ExecuteAsync(
                DrawerItemSortMode.ModifiedDate);

            Assert.Equal(DrawerItemSortMode.ModifiedDate, selectedDrawer.DrawerItemSortMode);
            Assert.True(selectedDrawer.IsDrawerSortByModifiedDate);
            Assert.False(selectedDrawer.IsFreeSort);
            Assert.Equal("修改日期", selectedDrawer.DrawerSortModeLabel);
            Assert.Equal(
                DrawerItemSortMode.ModifiedDate.ToString(),
                await drawerService.GetSettingAsync(
                    BoxViewModel.GetBoxSortModeSettingKey(createdBox.Id)));

            await selectedDrawer.ToggleTitleVisibilityCommand.ExecuteAsync(null);

            Assert.False(selectedDrawer.IsTitleVisible);
            Assert.Equal(
                bool.FalseString,
                await drawerService.GetSettingAsync(
                    BoxViewModel.GetTitleVisibilitySettingKey(createdBox.Id)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
