using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class MainWindowImportTests
{
    [Fact]
    public async Task ImportPathsAsync_PartialFailureRefreshesSuccessfulItemsAndReportsCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            await service.SetSettingAsync(MainViewModel.AboutPageShownSettingKey, bool.TrueString);
            var logger = new NoOpLogger();
            var launcher = new NoOpLauncher();
            var styles = new BoxVisualStyleStore(service, logger);
            var quickPanel = new QuickPanelViewModel(service, launcher, logger, styles);
            var viewModel = new MainViewModel(service, new TodoService(repository), launcher, logger,
                quickPanel, new UpdateService(logger), styles,
                new BoxPositionLockStateStore(service, logger), paths,
                new DataStorageMigrationService(paths, repository,
                    new StorageLocationStore(Path.Combine(root, StorageLocationStore.ConfigFileName))),
                new AutoHideSettingsStore(service));
            await viewModel.LoadAsync();
            var boxId = viewModel.SelectedBox!.Id;
            var good = Path.Combine(root, "good.txt");
            var blocked = Path.Combine(root, "blocked.txt");
            File.WriteAllText(good, "success");
            File.WriteAllText(blocked, "keep");
            var events = 0;
            viewModel.ItemsChanged += (_, _) => events++;
            using var fileLock = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None);

            await viewModel.ImportPathsAsync([good, blocked]);

            Assert.Single(viewModel.Items);
            Assert.Single(await service.GetItemsAsync(boxId));
            Assert.False(File.Exists(good));
            Assert.True(File.Exists(blocked));
            Assert.Equal(1, events);
            Assert.Contains("已导入 1 项", viewModel.StatusText);
            Assert.Contains("其余未导入", viewModel.StatusText);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
