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

public sealed class QuickPanelIncrementalRefreshTests
{
    [Fact]
    public async Task CrossBoxTransfer_RefreshesSourceAndTargetWithoutDuplicateQuickPanelItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.TransferTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawer = new DrawerService(paths, repository);
            await drawer.InitializeAsync();
            var first = await drawer.CreateBoxAsync("source", BoxType.Normal);
            var second = await drawer.CreateBoxAsync("target", BoxType.Normal);
            var path = Path.Combine(root, "payload.txt");
            await File.WriteAllTextAsync(path, "payload");
            var item = await drawer.ImportPathAsync(first.Id, path);
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var quick = new QuickPanelViewModel(drawer, launcher, logger, new BoxVisualStyleStore(drawer, logger));
            var todo = new TodoService(repository);
            var notifier = new NoOpShellChangeNotifier();
            var source = new DesktopBoxViewModel(first, drawer, todo, launcher, notifier, logger, BoxVisualStyle.Modern);
            var target = new DesktopBoxViewModel(second, drawer, todo, launcher, notifier, logger, BoxVisualStyle.Modern);
            await source.LoadAsync();
            await target.LoadAsync();
            await quick.EnsureLoadedAsync();
            var changes = new List<Guid>();
            var refreshes = new List<Task>();
            source.ItemsChanged += (_, _) => { changes.Add(first.Id); refreshes.Add(quick.RefreshBoxAsync(first.Id)); };
            target.ItemsChanged += (_, _) => { changes.Add(second.Id); refreshes.Add(quick.RefreshBoxAsync(second.Id)); };

            Assert.True(await target.DropDrawerItemAsync(item.Id, 0, 0));
            await source.RefreshAfterItemTransferAsync();
            await Task.WhenAll(refreshes);
            await quick.EnsureLoadedAsync();

            Assert.Equal(new[] { second.Id, first.Id }, changes);
            Assert.Empty(source.Items);
            Assert.Single(target.Items);
            var shown = Assert.Single(quick.Items);
            Assert.Equal(item.Id, shown.Id);
            Assert.Equal(second.Id, shown.Model.BoxId);
            Assert.True(File.Exists(shown.PathLabel));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefreshBoxAsync_ReplacesOnlyTheAffectedBoxItems()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawer.QuickPanelRefreshTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var firstBox = await drawerService.CreateBoxAsync("First", BoxType.Mapping);
            var secondBox = await drawerService.CreateBoxAsync("Second", BoxType.Mapping);
            var firstPath = Path.Combine(root, "first.txt");
            var secondPath = Path.Combine(root, "second.txt");
            var addedPath = Path.Combine(root, "added.txt");
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(secondPath, "second");
            await File.WriteAllTextAsync(addedPath, "added");
            await drawerService.ImportPathAsync(firstBox.Id, firstPath);
            await drawerService.ImportPathAsync(secondBox.Id, secondPath);
            var logger = new RecordingLogger();
            var viewModel = new QuickPanelViewModel(
                drawerService,
                new NoOpFileLauncher(),
                logger,
                new BoxVisualStyleStore(drawerService, logger));
            await viewModel.LoadAsync();
            var unaffectedItem = Assert.Single(
                viewModel.Items,
                item => item.Model.BoxId == secondBox.Id);

            await drawerService.ImportPathAsync(firstBox.Id, addedPath);
            await viewModel.RefreshBoxAsync(firstBox.Id);

            Assert.Equal(
                2,
                viewModel.Items.Count(item => item.Model.BoxId == firstBox.Id));
            Assert.Same(
                unaffectedItem,
                Assert.Single(
                    viewModel.Items,
                    item => item.Model.BoxId == secondBox.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NoOpShellChangeNotifier : IShellChangeNotifier
    {
        public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) => Task.CompletedTask;
        public void NotifyCreated(string path, bool isDirectory) { }
        public void NotifyFolderItemCreated(string path, bool isDirectory) { }
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

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
