using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class ArchivePageSelectionTests
{
    [Fact]
    public async Task LoadAsync_FirstLaunchShowsAboutPageAndRemembersIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        MainViewModel? viewModel = null;
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
            viewModel = MainViewModelFactory.Create(
                drawerService,
                new TodoService(repository),
                launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(),
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
                new AutoHideSettingsStore(drawerService));

            await viewModel.LoadAsync();

            Assert.True(viewModel.IsAboutPage);
            Assert.Null(viewModel.SelectedBox);
            Assert.Equal(
                bool.TrueString,
                await drawerService.GetSettingAsync(MainViewModel.AboutPageShownSettingKey));

            viewModel.ShowDashboardCommand.Execute(null);
            await viewModel.LoadAsync();

            Assert.False(viewModel.IsAboutPage);
        }
        finally
        {
            if (viewModel is not null) await viewModel.WaitForPendingLoadsAsync();
            await TestDirectoryCleanup.DeleteAsync(root);
        }
    }

    [Fact]
    public async Task ShowArchiveCommand_ClearsSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        MainViewModel? viewModel = null;
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
            viewModel = MainViewModelFactory.Create(
                drawerService,
                new TodoService(repository),
                launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(),
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
                new AutoHideSettingsStore(drawerService));

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
            Assert.NotNull(viewModel.SelectedBox);

            await viewModel.ShowArchiveCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsArchivePage);
            Assert.Null(viewModel.SelectedBox);

            viewModel.ShowDashboardCommand.Execute(null);

            Assert.False(viewModel.IsArchivePage);
        }
        finally
        {
            if (viewModel is not null) await viewModel.WaitForPendingLoadsAsync();
            await TestDirectoryCleanup.DeleteAsync(root);
        }
    }

    [Fact]
    public async Task ShowSettingsAndAboutCommands_ClearSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        MainViewModel? viewModel = null;
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
            viewModel = MainViewModelFactory.Create(
                drawerService,
                new TodoService(repository),
                launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(),
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
                new AutoHideSettingsStore(drawerService));

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
            Assert.NotNull(viewModel.SelectedBox);

            await viewModel.ShowSettingsCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsSettingsPage);
            Assert.Null(viewModel.SelectedBox);

            // Re-select a box so the About page can be verified the same way.
            viewModel.ShowDashboardCommand.Execute(null);
            await SelectFirstBoxAsync(viewModel);
            Assert.NotNull(viewModel.SelectedBox);

            viewModel.ShowAboutCommand.Execute(null);

            Assert.True(viewModel.IsAboutPage);
            Assert.Null(viewModel.SelectedBox);
        }
        finally
        {
            if (viewModel is not null) await viewModel.WaitForPendingLoadsAsync();
            await TestDirectoryCleanup.DeleteAsync(root);
        }
    }

    private static async Task SelectFirstBoxAsync(MainViewModel viewModel)
    {
        viewModel.SelectedBox = viewModel.Boxes.First();
        await viewModel.WaitForPendingLoadsAsync();
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
