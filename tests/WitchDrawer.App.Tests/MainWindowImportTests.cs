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

public sealed class MainWindowImportTests
{
    [Fact]
    public async Task RenameAndStyleChange_KeepCollectionsAndPublishOnlyAffectedPresentation()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.PresentationTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            await service.SetSettingAsync(MainViewModel.AboutPageShownSettingKey, bool.TrueString);
            var first = (await service.GetBoxesAsync()).First(box => box.Type == BoxType.Normal);
            var source = Path.Combine(root, "synthetic.txt");
            await File.WriteAllTextAsync(source, "fixture");
            await service.ImportPathAsync(first.Id, source);
            var logger = new NoOpLogger();
            var launcher = new NoOpLauncher();
            var styles = new BoxVisualStyleStore(service, logger);
            var quickPanel = new QuickPanelViewModel(service, launcher, logger, styles);
            var model = MainViewModelFactory.Create(service, new TodoService(repository), launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(), logger,
                quickPanel, new UpdateService(logger), styles,
                new BoxPositionLockStateStore(service, logger), paths,
                new DataStorageMigrationService(paths, repository,
                    new StorageLocationStore(Path.Combine(root, StorageLocationStore.ConfigFileName))),
                new AutoHideSettingsStore(service));
            await model.LoadAsync();
            Assert.Equal(first.Id, model.SelectedBox!.Id);
            var originalBoxes = model.Boxes.ToArray();
            var originalItem = Assert.Single(model.Items);
            var requests = new List<BoxesChangedEventArgs>();
            model.BoxesChanged += (_, change) => requests.Add(change);

            await model.RenameSelectedBoxCommand.ExecuteAsync("Renamed");
            await model.SetSelectedBoxVisualStyleCommand.ExecuteAsync(
                BoxVisualStyleCatalog.GetOption(BoxVisualStyle.Pixel));

            Assert.Equal(originalBoxes, model.Boxes.ToArray());
            Assert.Same(originalItem, Assert.Single(model.Items));
            Assert.Equal("Renamed", originalItem.BoxName);
            Assert.True(originalItem.IsPixelated);
            Assert.Equal("Renamed", model.SelectedBox.Name);
            Assert.Equal("Renamed", (await service.GetBoxesAsync()).Single(box => box.Id == first.Id).Name);
            Assert.Equal(2, requests.Count);
            Assert.All(requests, request =>
            {
                Assert.Equal(first.Id, request.BoxId);
                Assert.True(request.RefreshRequest.PresentationOnly);
                Assert.Equal(new[] { first.Id }, request.RefreshRequest.BoxIds);
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

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
            var viewModel = MainViewModelFactory.Create(service, new TodoService(repository), launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(), logger,
                quickPanel, new UpdateService(logger), styles,
                new BoxPositionLockStateStore(service, logger), paths,
                new DataStorageMigrationService(paths, repository,
                    new StorageLocationStore(Path.Combine(root, StorageLocationStore.ConfigFileName))),
                new AutoHideSettingsStore(service));
            await viewModel.LoadAsync();
            var boxId = viewModel.SelectedBox!.Id;
            var good = Path.Combine(root, "good.txt");
            var alsoGood = Path.Combine(root, "also-good.txt");
            var blocked = Path.Combine(root, "blocked.txt");
            File.WriteAllText(good, "success");
            File.WriteAllText(alsoGood, "success2");
            File.WriteAllText(blocked, "keep");
            var events = 0;
            service.Changes.ContentChanged += (_, _) => events++;
            using var fileLock = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None);

            await viewModel.ImportPathsAsync([good, alsoGood, blocked]);

            Assert.Equal(2, viewModel.Items.Count);
            Assert.Equal(2, (await service.GetItemsAsync(boxId)).Count);
            Assert.False(File.Exists(good));
            Assert.False(File.Exists(alsoGood));
            Assert.True(File.Exists(blocked));
            Assert.Equal(1, events);
            Assert.Contains("已导入 2 项", viewModel.StatusText);
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
