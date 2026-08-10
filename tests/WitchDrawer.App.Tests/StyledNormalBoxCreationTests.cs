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

public sealed class StyledNormalBoxCreationTests
{
    [Fact]
    public async Task PixelStyleCreation_PreservesNormalBoxFileBehavior()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(
                drawerService,
                launcher,
                logger,
                visualStyleStore);
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
            var existingIds = (await drawerService.GetBoxesAsync())
                .Select(box => box.Id)
                .ToHashSet();

            await viewModel.CreatePixelBoxCommand.ExecuteAsync(null);

            var createdBox = Assert.Single(
                await drawerService.GetBoxesAsync(),
                box => !existingIds.Contains(box.Id));
            Assert.Equal(BoxType.Normal, createdBox.Type);
            Assert.Equal(
                BoxVisualStyle.Pixel,
                await visualStyleStore.LoadAsync(createdBox));
            var selectedBox = Assert.IsType<BoxViewModel>(viewModel.SelectedBox);
            await selectedBox.LoadTitleVisibilityAsync();
            Assert.True(selectedBox.IsTitleVisible);
            await selectedBox.LoadFileNameVisibilityAsync();
            Assert.True(selectedBox.IsFileNameVisible);
            Assert.Equal(
                BoxViewModel.GetFileNameVisibilitySettingKey(createdBox.Id),
                DesktopBoxViewModel.GetFileNameVisibilitySettingKey(createdBox.Id));

            await selectedBox.ToggleTitleVisibilityCommand.ExecuteAsync(null);
            await selectedBox.ToggleFileNameVisibilityCommand.ExecuteAsync(null);

            Assert.False(selectedBox.IsTitleVisible);
            Assert.Equal(
                bool.FalseString,
                await drawerService.GetSettingAsync(
                    BoxViewModel.GetTitleVisibilitySettingKey(createdBox.Id)));
            Assert.False(selectedBox.IsFileNameVisible);
            Assert.Equal(
                bool.FalseString,
                await drawerService.GetSettingAsync(
                    BoxViewModel.GetFileNameVisibilitySettingKey(createdBox.Id)));
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
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
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
