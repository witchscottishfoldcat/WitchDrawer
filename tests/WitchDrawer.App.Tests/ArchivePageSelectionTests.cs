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
    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    TestCleanup.DeleteDirectory(root);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_FirstLaunchShowsAboutPageAndRemembersIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var viewModel = new MainViewModel(
                drawerService,
                new RustTodoService(drawerService),
                launcher,
                logger,
                quickPanel,
                new RustUpdateService(drawerService, logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    ct => drawerService.CheckpointAsync(ct),
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

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
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ShowArchiveCommand_ClearsSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var viewModel = new MainViewModel(
                drawerService,
                new RustTodoService(drawerService),
                launcher,
                logger,
                quickPanel,
                new RustUpdateService(drawerService, logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    ct => drawerService.CheckpointAsync(ct),
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

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
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ShowSettingsAndAboutCommands_ClearSelectedBox()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore);
            var viewModel = new MainViewModel(
                drawerService,
                new RustTodoService(drawerService),
                launcher,
                logger,
                quickPanel,
                new RustUpdateService(drawerService, logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    ct => drawerService.CheckpointAsync(ct),
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

            await viewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
            Assert.NotNull(viewModel.SelectedBox);

            viewModel.ShowSettingsCommand.Execute(null);

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
            CleanupTempRoot(root);
        }
    }

    private static async Task SelectFirstBoxAsync(MainViewModel viewModel)
    {
        viewModel.SelectedBox = viewModel.Boxes.First();
        // SelectedBox setter queues a fire-and-forget items load; give it a moment
        // so the SQLite connection is released before temp directory cleanup.
        await Task.Delay(200);
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
