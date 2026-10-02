using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxContentRefreshTests
{
    [Fact]
    public async Task LoadAsync_CoalescedTransfersWithNameConflict_RefreshesPathAndRetainsUnchangedItems()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawer = new DrawerService(paths, repository);
            await drawer.InitializeAsync();
            var source = await drawer.CreateBoxAsync("source", BoxType.Normal);
            var target = await drawer.CreateBoxAsync("target", BoxType.Normal);
            var firstPath = WriteFile(root, "first", "same.txt");
            var secondPath = WriteFile(root, "second", "same.txt");
            var first = await drawer.ImportPathAsync(source.Id, firstPath);
            await drawer.ImportPathAsync(target.Id, secondPath);
            var untouched = await drawer.ImportPathAsync(source.Id, WriteFile(root, "first", "untouched.txt"));
            var viewModel = CreateViewModel(source, drawer, repository);
            await viewModel.LoadAsync();
            var original = viewModel.Items.Single(item => item.Id == first.Id);
            var originalUntouched = viewModel.Items.Single(item => item.Id == untouched.Id);

            // Content notifications can be coalesced before this surface next refreshes.
            await drawer.MoveItemToBoxAsync(first.Id, target.Id);
            await drawer.MoveItemToBoxAsync(first.Id, source.Id);
            await viewModel.LoadAsync();

            var current = viewModel.Items.Single(item => item.Id == first.Id);
            var stored = (await drawer.GetItemsAsync(source.Id)).Single(item => item.Id == first.Id);
            Assert.Equal("same (1).txt", current.DisplayName);
            Assert.Equal(stored, current.Model);
            Assert.Equal(stored.StoredPath, current.PathLabel);
            Assert.True(File.Exists(current.PathLabel));
            Assert.NotSame(original, current);
            Assert.Same(originalUntouched, viewModel.Items.Single(item => item.Id == untouched.Id));

            await viewModel.LoadAsync();
            Assert.Same(current, viewModel.Items.Single(item => item.Id == first.Id));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_PositionOnlyChanges_UpdateModelWithoutReplacingItemOrResettingCollection()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawer = new DrawerService(paths, repository);
            await drawer.InitializeAsync();
            var box = await drawer.CreateBoxAsync("box", BoxType.Mapping);
            var item = await drawer.ImportPathAsync(box.Id, WriteFile(root, "source", "item.txt"), 0, 0);
            var viewModel = CreateViewModel(box, drawer, repository);
            await viewModel.LoadAsync();
            var original = Assert.Single(viewModel.Items);
            var collectionChanges = 0;
            viewModel.Items.CollectionChanged += (_, _) => collectionChanges++;

            await drawer.UpdateItemGridPositionAsync(item.Id, 2, 3);
            await viewModel.LoadAsync();

            Assert.Same(original, Assert.Single(viewModel.Items));
            Assert.Equal(0, collectionChanges);
            Assert.Equal((2, 3), (original.GridColumn, original.GridRow));
            Assert.Equal((await drawer.GetItemsAsync(box.Id)).Single(), original.Model);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static DesktopBoxViewModel CreateViewModel(Box box, DrawerService drawer, DrawerRepository repository)
        => new(box, drawer, new TodoService(repository), new NoOpLauncher(), new NoOpNotifier(),
            NullAppLogger.Instance, BoxVisualStyle.Modern);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.ContentRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteFile(string root, string directory, string name)
    {
        var parent = Path.Combine(root, directory);
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, name);
        File.WriteAllText(path, name);
        return path;
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpNotifier : IShellChangeNotifier
    {
        public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) => Task.CompletedTask;
        public void NotifyCreated(string path, bool isDirectory) { }
        public void NotifyFolderItemCreated(string path, bool isDirectory) { }
    }
}
