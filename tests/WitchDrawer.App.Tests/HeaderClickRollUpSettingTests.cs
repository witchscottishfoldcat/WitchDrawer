using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class HeaderClickRollUpSettingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    [InlineData("False", false)]
    [InlineData("True", true)]
    public async Task LoadToggleAndRestart_PreservePreference(string? saved, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            if (saved is not null)
            {
                await service.SetSettingAsync(MainViewModel.HeaderClickRollUpSettingKey, saved);
            }

            MainViewModel CreateViewModel()
            {
                var logger = new RecordingLogger();
                var launcher = new NoOpFileLauncher();
                var visualStyles = new BoxVisualStyleStore(service, logger);
                return new MainViewModel(service, new TodoService(repository), launcher, logger,
                    new QuickPanelViewModel(service, launcher, logger, visualStyles),
                    new UpdateService(logger), visualStyles, new BoxPositionLockStateStore(service, logger),
                    paths, new DataStorageMigrationService(paths, repository,
                        new StorageLocationStore(Path.Combine(root, "storage-location.json"))));
            }

            var first = CreateViewModel();
            await first.LoadAsync();
            Assert.Equal(expected, first.IsHeaderClickRollUpEnabled);
            await first.ToggleHeaderClickRollUpCommand.ExecuteAsync(null);
            Assert.Equal(!expected, first.IsHeaderClickRollUpEnabled);
            Assert.Equal((!expected).ToString(),
                await service.GetSettingAsync(MainViewModel.HeaderClickRollUpSettingKey));

            // A fresh view model represents a new application session, without carrying UI state.
            var restarted = CreateViewModel();
            await restarted.LoadAsync();
            Assert.Equal(!expected, restarted.IsHeaderClickRollUpEnabled);
            await restarted.ToggleHeaderClickRollUpCommand.ExecuteAsync(null);
            Assert.Equal(expected.ToString(),
                await service.GetSettingAsync(MainViewModel.HeaderClickRollUpSettingKey));
            var restartedAgain = CreateViewModel();
            await restartedAgain.LoadAsync();
            Assert.Equal(expected, restartedAgain.IsHeaderClickRollUpEnabled);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
