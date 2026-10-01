using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class BoxContentChangeTests
{
    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Mapping)]
    public async Task ImportMoveAndDelete_NotifyOnlyCommittedAffectedBoxes(BoxType type)
    {
        using var workspace = new Workspace();
        await workspace.Drawer.InitializeAsync();
        var first = await workspace.Drawer.CreateBoxAsync("first", type);
        var second = await workspace.Drawer.CreateBoxAsync("second", type);
        var path = Path.Combine(workspace.Root, "payload.txt");
        await File.WriteAllTextAsync(path, "payload");
        var changes = new List<IReadOnlyList<Guid>>();
        workspace.Drawer.Changes.ContentChanged += (_, e) => changes.Add(e.BoxIds);

        var item = await workspace.Drawer.ImportPathAsync(first.Id, path);
        Assert.Equal(new[] { first.Id }, Assert.Single(changes));
        await workspace.Drawer.MoveItemToBoxAsync(item.Id, second.Id);
        Assert.Equal(new[] { first.Id, second.Id }, changes[1]);
        Assert.Empty(await workspace.Drawer.GetItemsAsync(first.Id));
        Assert.Single(await workspace.Drawer.GetItemsAsync(second.Id));
        await workspace.Drawer.DeleteItemAsync(item.Id);
        Assert.Equal(new[] { second.Id }, changes[2]);
        Assert.Empty(await workspace.Drawer.GetItemsAsync(second.Id));
        Assert.Equal("payload", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FailedOrCanceledMutation_DoesNotPublishAChange()
    {
        using var workspace = new Workspace();
        await workspace.Drawer.InitializeAsync();
        var box = await workspace.Drawer.CreateBoxAsync("normal", BoxType.Normal);
        var changes = 0;
        workspace.Drawer.Changes.ContentChanged += (_, _) => changes++;
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.Drawer.ImportPathAsync(box.Id,
            Path.Combine(workspace.Root, "missing.txt")));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.Drawer.DeleteBoxAsync(box.Id, canceled.Token));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task FailingSubscriber_DoesNotTurnCommittedImportIntoFailure()
    {
        using var workspace = new Workspace();
        await workspace.Drawer.InitializeAsync();
        var box = await workspace.Drawer.CreateBoxAsync("normal", BoxType.Normal);
        var path = Path.Combine(workspace.Root, "payload.txt");
        await File.WriteAllTextAsync(path, "payload");
        workspace.Drawer.Changes.ContentChanged += (_, _) => throw new InvalidOperationException("view failed");
        var delivered = 0;
        workspace.Drawer.Changes.ContentChanged += (_, _) => delivered++;

        var item = await workspace.Drawer.ImportPathAsync(box.Id, path);

        Assert.Equal(1, delivered);
        Assert.Equal(item.Id, Assert.Single(await workspace.Drawer.GetItemsAsync(box.Id)).Id);
        Assert.True(File.Exists(item.StoredPath));
    }

    [Fact]
    public async Task TodoChanges_UseTheSameNotifierAsFileChanges()
    {
        using var workspace = new Workspace();
        await workspace.Drawer.InitializeAsync();
        var box = await workspace.Drawer.CreateBoxAsync("todo", BoxType.Todo);
        var todo = new TodoService(workspace.Repository, changes: workspace.Drawer.Changes);
        var changes = new List<IReadOnlyList<Guid>>();
        workspace.Drawer.Changes.ContentChanged += (_, e) => changes.Add(e.BoxIds);
        var item = await todo.AddTodoAsync(box.Id, "test");
        await todo.SetCompletedAsync(item.Id, true);
        await todo.ArchiveCompletedAsync(box.Id);
        await todo.RestoreArchivedAsync(item.Id);
        var undo = await todo.DeleteWithUndoAsync(item.Id);
        await todo.UndoDeleteAsync(undo.Token);
        Assert.Equal(6, changes.Count);
        Assert.All(changes, ids => Assert.Equal(new[] { box.Id }, ids));
    }

    private sealed class Workspace : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "WitchDrawer.ContentChanges", Guid.NewGuid().ToString("N"));
        internal DrawerRepository Repository { get; }
        internal DrawerService Drawer { get; }
        internal Workspace()
        {
            var paths = new AppPaths(Path.Combine(Root, "data"));
            Repository = new DrawerRepository(paths.DatabasePath);
            Drawer = new DrawerService(paths, Repository);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
