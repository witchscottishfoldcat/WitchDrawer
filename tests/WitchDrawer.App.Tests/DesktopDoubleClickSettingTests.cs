using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopDoubleClickSettingTests
{
    [Fact]
    public async Task LoadAndToggle_PersistsDesktopDoubleClickSetting()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            await drawerService.SetSettingAsync(SettingsViewModel.DesktopDoubleClickSettingKey, bool.TrueString);
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var viewModel = MainViewModelFactory.Create(
                drawerService,
                new TodoService(repository),
                launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(),
                logger,
                new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore),
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
            Assert.True(viewModel.Settings.IsDesktopDoubleClickEnabled);

            await viewModel.Settings.ToggleDesktopDoubleClickCommand.ExecuteAsync(null);

            Assert.False(viewModel.Settings.IsDesktopDoubleClickEnabled);
            Assert.Equal(
                bool.FalseString,
                await drawerService.GetSettingAsync(SettingsViewModel.DesktopDoubleClickSettingKey));
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
