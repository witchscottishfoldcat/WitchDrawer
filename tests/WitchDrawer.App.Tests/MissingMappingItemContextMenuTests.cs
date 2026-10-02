using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WitchDrawer.App.Features.ItemContextMenu;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class MissingMappingItemContextMenuTests
{
    [Fact]
    public Task MissingMappingSource_StillOffersRemovalAndRemovesOnlyTheReference() =>
        RunOnStaAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.MissingMappingMenuTests", Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new AppPaths(Path.Combine(root, "data"));
                var repository = new DrawerRepository(paths.DatabasePath);
                var drawer = new DrawerService(paths, repository);
                await drawer.InitializeAsync();
                var box = await drawer.CreateBoxAsync("Mapping", BoxType.Mapping);
                var source = Path.Combine(root, "missing.exe");
                var retainedSource = Path.Combine(root, "retained.txt");
                await File.WriteAllTextAsync(source, "mapped source");
                await File.WriteAllTextAsync(retainedSource, "keep this file");
                var imported = await drawer.ImportPathAsync(box.Id, source);
                var retained = await drawer.ImportPathAsync(box.Id, retainedSource);
                var host = new DesktopBoxViewModel(box, drawer, new TodoService(repository),
                    new UnexpectedLauncher(), new UnexpectedShellChangeNotifier(), NullAppLogger.Instance,
                    BoxVisualStyle.Modern);
                await host.LoadAsync();
                var item = host.Items.Single(candidate => candidate.Id == imported.Id);

                File.Delete(source);
                var pathState = DrawerItemContextMenuCoordinator.InspectPath(item.PathLabel);
                Assert.False(pathState.Exists);
                var menu = DrawerItemContextMenuCoordinator.CreateMenuForPath(
                    item.PathLabel, pathState, host.IsMappingBox, host.IsPixelStyle, 0, 0);
                Assert.NotNull(menu);
                try
                {
                    AssertPathActionsAreHidden(menu);
                    var remove = GetButton(menu, "RemoveButton");
                    Assert.Equal(Visibility.Visible, remove.Visibility);
                    Assert.True(remove.IsEnabled);
                    Assert.Equal("移除引用", remove.Content);
                }
                finally { menu.Close(); }

                await host.DeleteItemCommand.ExecuteAsync(item);

                Assert.Null(await repository.GetItemAsync(imported.Id));
                Assert.Equal(retained.Id, Assert.Single(host.Items).Id);
                Assert.Equal(retained.Id, Assert.Single(await drawer.GetItemsAsync(box.Id)).Id);
                Assert.False(File.Exists(source));
                Assert.Equal("keep this file", await File.ReadAllTextAsync(retainedSource));
                Assert.Equal("已移除引用 missing.exe", host.StatusText);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });

    [Fact]
    public void MissingStoredItem_DoesNotOfferReferenceRemoval()
    {
        var menu = DrawerItemContextMenuCoordinator.CreateMenuForPath(
            @"C:\unavailable\stored.exe", (Exists: false, IsDirectory: false),
            isMappingBox: false, isPixelStyle: false, 0, 0);

        Assert.Null(menu);
    }

    [Theory]
    [InlineData(true, false, true, "移除引用")]
    [InlineData(false, false, true, "移出收纳盒")]
    [InlineData(true, true, false, "移除引用")]
    public Task ExistingPath_KeepsItsApplicableActions(
        bool isMappingBox,
        bool isDirectory,
        bool canRunAsAdministrator,
        string removeLabel) =>
        RunOnStaAsync(() =>
        {
            var menu = DrawerItemContextMenuCoordinator.CreateMenuForPath(
                @"C:\available\item.exe", (Exists: true, IsDirectory: isDirectory),
                isMappingBox, isPixelStyle: false, 0, 0);
            Assert.NotNull(menu);
            try
            {
                Assert.Equal(Visibility.Visible, GetButton(menu, "OpenButton").Visibility);
                Assert.Equal(Visibility.Visible, GetButton(menu, "RevealButton").Visibility);
                Assert.Equal(canRunAsAdministrator ? Visibility.Visible : Visibility.Collapsed,
                    GetButton(menu, "RunAsAdministratorButton").Visibility);
                Assert.Equal(removeLabel, GetButton(menu, "RemoveButton").Content);
            }
            finally { menu.Close(); }
            return Task.CompletedTask;
        });

    private static void AssertPathActionsAreHidden(DrawerItemContextMenuWindow menu)
    {
        Assert.Equal(Visibility.Collapsed, GetButton(menu, "OpenButton").Visibility);
        Assert.Equal(Visibility.Collapsed, GetButton(menu, "RevealButton").Visibility);
        Assert.Equal(Visibility.Collapsed, GetButton(menu, "RunAsAdministratorButton").Visibility);
    }

    private static Button GetButton(DrawerItemContextMenuWindow menu, string name) =>
        Assert.IsType<Button>(menu.FindName(name));

    private static Task RunOnStaAsync(Func<Task> test)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); finished.SetResult(); }
                catch (Exception exception) { finished.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class UnexpectedLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reference removal must not open a file.");
    }

    private sealed class UnexpectedShellChangeNotifier : IShellChangeNotifier
    {
        public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) =>
            throw new InvalidOperationException("Reference removal must not notify a file mutation.");

        public void NotifyCreated(string path, bool isDirectory) =>
            throw new InvalidOperationException("Reference removal must not notify a file mutation.");

        public void NotifyFolderItemCreated(string itemPath, bool isDirectory) =>
            throw new InvalidOperationException("Reference removal must not notify a file mutation.");
    }
}
