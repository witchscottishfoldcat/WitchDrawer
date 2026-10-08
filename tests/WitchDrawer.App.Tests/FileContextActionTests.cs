using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WitchDrawer.App.Features.ItemContextMenu;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class FileContextActionTests
{
    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Drawer)]
    [InlineData(BoxType.Mapping)]
    public Task RenameThenRemove_OffersBothActionsAndPreservesFile(BoxType type) => Sta(async () =>
    {
        using var w = await Workspace.CreateAsync(type);
        var imported = await w.Drawer.ImportPathAsync(w.Box.Id, w.Source);
        await w.Host.LoadAsync();
        var item = Assert.Single(w.Host.Items);
        var menu = DrawerItemContextMenuCoordinator.CreateMenuForPath(item.PathLabel,
            DrawerItemContextMenuCoordinator.InspectPath(item.PathLabel), w.Host.IsMappingBox, false, 0, 0);
        try
        {
            foreach (var name in new[] { "RenameButton", "RemoveButton" })
            {
                Assert.Equal(Visibility.Visible, Button(menu, name).Visibility);
                Assert.True(Button(menu, name).IsEnabled);
            }
        }
        finally { menu.Close(); }
        await w.Host.RenameFileItemAsync(item, "renamed.txt");
        var renamed = (await w.Repository.GetItemAsync(imported.Id))!;
        Assert.Equal("renamed.txt", renamed.DisplayName);
        using var coordinator = new DrawerItemContextMenuCoordinator(w.Host, new FakeClipboard());
        await coordinator.ExecuteAsync(DrawerItemContextAction.RemoveFromBox, Assert.Single(w.Host.Items));
        Assert.Empty(w.Host.Items);
        Assert.Null(await w.Repository.GetItemAsync(imported.Id));
        var restored = type == BoxType.Mapping ? w.Source : Path.Combine(w.Root, "renamed.txt");
        Assert.Equal("payload", File.ReadAllText(restored));
    });

    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Mapping)]
    public Task Paste_UsesCopyForStorageAndReferencesForMapping(BoxType type) => Sta(async () =>
    {
        using var w = await Workspace.CreateAsync(type);
        var clipboard = new FakeClipboard { Paths = [w.Source] };
        using var menu = new DrawerItemContextMenuCoordinator(w.Host, clipboard);
        await menu.ExecuteAsync(DrawerItemContextAction.Paste, null);
        var item = Assert.Single(await w.Drawer.GetItemsAsync(w.Box.Id));
        Assert.Equal("payload", File.ReadAllText(w.Source));
        if (type == BoxType.Mapping) Assert.Null(item.StoredPath);
        else Assert.Equal("payload", File.ReadAllText(item.StoredPath!));
        Assert.Equal(w.Source, item.SourcePath);
    });

    [Fact]
    public Task CopyAndCopyPath_UseLatestPathWithoutMutatingFile() => Sta(async () =>
    {
        using var w = await Workspace.CreateAsync(BoxType.Normal);
        var item = await w.Drawer.ImportPathAsync(w.Box.Id, w.Source);
        await w.Host.LoadAsync();
        var oldViewModel = Assert.Single(w.Host.Items);
        var renamed = await w.Drawer.RenameItemAsync(w.Box.Id, item.Id, "renamed.txt");
        var clipboard = new FakeClipboard();
        using var menu = new DrawerItemContextMenuCoordinator(w.Host, clipboard);
        await menu.ExecuteAsync(DrawerItemContextAction.Copy, oldViewModel);
        Assert.Equal(new[] { renamed.StoredPath }, clipboard.Paths);
        await menu.ExecuteAsync(DrawerItemContextAction.CopyPath, oldViewModel);
        Assert.Equal(renamed.StoredPath, clipboard.Text);
        Assert.Equal("payload", File.ReadAllText(renamed.StoredPath!));
    });

    [Fact]
    public Task MissingStoredItem_MenuCleanupRemovesOnlyRecord() => Sta(async () =>
    {
        using var w = await Workspace.CreateAsync(BoxType.Normal);
        var item = await w.Drawer.ImportPathAsync(w.Box.Id, w.Source);
        await w.Host.LoadAsync();
        var vm = Assert.Single(w.Host.Items);
        File.Delete(item.StoredPath!);
        using var menu = new DrawerItemContextMenuCoordinator(w.Host, new FakeClipboard());
        await menu.ExecuteAsync(DrawerItemContextAction.RemoveFromBox, vm);
        Assert.Empty(w.Host.Items);
        Assert.Null(await w.Repository.GetItemAsync(item.Id));
        Assert.False(File.Exists(w.Source));
        Assert.Contains("收纳记录", w.Host.StatusText);
    });

    [Fact]
    public Task PendingMenu_CancelsBeforeSlowInspectionCompletes() => Sta(async () =>
    {
        using var w = await Workspace.CreateAsync(BoxType.Mapping);
        await w.Drawer.ImportPathAsync(w.Box.Id, w.Source);
        await w.Host.LoadAsync();
        var inspection = new TaskCompletionSource<(bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var menu = new DrawerItemContextMenuCoordinator(w.Host, new FakeClipboard(),
            inspect: _ => inspection.Task, cursor: () => (12, 34));
        var request = menu.ShowAsync(Assert.Single(w.Host.Items));
        Assert.True(menu.IsMenuActive);
        menu.CancelPendingMenu();
        inspection.SetResult((true, false));
        await request;
        Assert.False(menu.IsMenuActive);
    });

    [Fact]
    public Task BlankBoxMenu_OffersPasteAndNoItemActions() => Sta(() =>
    {
        var menu = new DrawerItemContextMenuWindow(false, false, false, 0, 0, false, canPaste: true, hasItem: false);
        try
        {
            Assert.True(Button(menu, "PasteButton").IsEnabled);
            foreach (var name in new[] { "OpenButton", "CopyButton", "CopyPathButton", "RenameButton", "RemoveButton" })
                Assert.Equal(Visibility.Collapsed, Button(menu, name).Visibility);
        }
        finally { menu.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShortcutMenu_OffersTargetAndShortcutLocations() => Sta(() =>
    {
        var menu = DrawerItemContextMenuCoordinator.CreateMenuForPath(@"C:\shortcut.lnk", (true, false), false, false, 0, 0);
        try
        {
            Assert.Equal("打开目标所在位置", Button(menu, "RevealButton").Content);
            Assert.Equal(Visibility.Visible, Button(menu, "ShortcutLocationButton").Visibility);
        }
        finally { menu.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(Key.C, ModifierKeys.Control, (int)DrawerItemContextAction.Copy)]
    [InlineData(Key.C, ModifierKeys.Control | ModifierKeys.Shift, (int)DrawerItemContextAction.CopyPath)]
    [InlineData(Key.V, ModifierKeys.Control, (int)DrawerItemContextAction.Paste)]
    [InlineData(Key.F2, ModifierKeys.None, (int)DrawerItemContextAction.Rename)]
    public void KeyboardActions_MatchMenus(Key key, ModifierKeys modifiers, int expected)
        => Assert.Equal((DrawerItemContextAction)expected, DesktopBoxWindow.GetFileKeyboardAction(key, modifiers));

    private static Button Button(Window menu, string name) => Assert.IsType<Button>(menu.FindName(name));
    private static Task Sta(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); done.SetResult(); }
                catch (Exception exception) { done.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private sealed class FakeClipboard : IFileClipboard
    {
        public string[] Paths { get; set; } = [];
        public string? Text { get; private set; }
        public string[] ReadPaths() => Paths;
        public void CopyFile(string path) => Paths = [path];
        public void CopyPath(string path) => Text = path;
    }
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "WitchDrawer.FileMenus", Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "payload.txt");
        public DrawerRepository Repository { get; }
        public DrawerService Drawer { get; }
        public Box Box { get; private set; } = null!;
        public DesktopBoxViewModel Host { get; private set; } = null!;
        private Workspace()
        {
            var paths = new AppPaths(Path.Combine(Root, "data"));
            Repository = new DrawerRepository(paths.DatabasePath);
            Drawer = new DrawerService(paths, Repository);
        }
        public static async Task<Workspace> CreateAsync(BoxType type)
        {
            var w = new Workspace(); await w.Drawer.InitializeAsync();
            w.Box = await w.Drawer.CreateBoxAsync("files", type);
            File.WriteAllText(w.Source, "payload");
            w.Host = new DesktopBoxViewModel(w.Box, w.Drawer, new TodoService(w.Repository),
                new NoOpLauncher(), new NoOpNotifier(), NullAppLogger.Instance, BoxVisualStyle.Modern);
            return w;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class NoOpLauncher : IFileLauncher { public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class NoOpNotifier : IShellChangeNotifier
    {
        public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) => Task.CompletedTask;
        public void NotifyCreated(string path, bool directory) { }
        public void NotifyFolderItemCreated(string path, bool directory) { }
    }
}
