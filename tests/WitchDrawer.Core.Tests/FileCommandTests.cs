using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class FileCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copy_PreservesSourceAndSuffixesConflicts(bool directory)
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("source", "content", directory);
        var first = await w.Service.CopyPathToBoxAsync(w.Box.Id, source);
        var second = await w.Service.CopyPathToBoxAsync(w.Box.Id, source);
        Assert.Equal(directory ? "source (1)" : "source (1).txt", second.DisplayName);
        Assert.Equal("content", w.Read(source, directory));
        Assert.Equal("content", w.Read(first.StoredPath!, directory));
        Assert.Equal("content", w.Read(second.StoredPath!, directory));
        Assert.Equal(2, (await w.Service.GetItemsAsync(w.Box.Id)).Count);
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task CopyManagedItem_KeepsOriginalRestoreDirectory()
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("original", "payload");
        var original = await w.Service.ImportPathAsync(w.Box.Id, source);
        var second = await w.Service.CreateBoxAsync("second", BoxType.Normal);
        var copied = await w.Service.CopyPathToBoxAsync(second.Id, original.StoredPath!);
        Assert.Equal(source, copied.SourcePath);
        Assert.Equal(source, (await w.Service.DeleteItemAsync(copied.Id)).RestoredPath);
        Assert.Equal("payload", File.ReadAllText(original.StoredPath!));
        Assert.Equal("payload", File.ReadAllText(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rename_UpdatesActualNameAndRestoreLocation(bool directory)
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("old", "payload", directory);
        var item = await w.Service.ImportPathAsync(w.Box.Id, source);
        var renamed = await w.Service.RenameItemAsync(w.Box.Id, item.Id, directory ? "new" : "new.txt");
        Assert.False(File.Exists(item.StoredPath) || Directory.Exists(item.StoredPath));
        Assert.Equal("payload", w.Read(renamed.StoredPath!, directory));
        Assert.Equal(renamed.StoredPath, (await w.Repository.GetItemAsync(item.Id))!.StoredPath);
        var restored = await w.Service.DeleteItemAsync(item.Id);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(source)!, renamed.DisplayName), restored.RestoredPath);
        Assert.Equal("payload", w.Read(restored.RestoredPath!, directory));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rename_CaseOnlyChangesActualDirectoryEntry(bool directory)
    {
        using var w = await Workspace.CreateAsync();
        var item = await w.Service.ImportPathAsync(w.Box.Id, w.Source("Mixed", "payload", directory));
        var name = directory ? "MIXED" : "MIXED.txt";
        var renamed = await w.Service.RenameItemAsync(w.Box.Id, item.Id, name);
        Assert.Contains(Directory.EnumerateFileSystemEntries(w.Box.StoragePath!), path => Path.GetFileName(path) == name);
        Assert.Equal("payload", w.Read(renamed.StoredPath!, directory));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task Rename_ConflictSuffixesAndPreservesBothFiles()
    {
        using var w = await Workspace.CreateAsync();
        var first = await w.Service.ImportPathAsync(w.Box.Id, w.Source("first", "first"));
        var second = await w.Service.ImportPathAsync(w.Box.Id, w.Source("second", "second"));
        var renamed = await w.Service.RenameItemAsync(w.Box.Id, first.Id, "second.txt");
        Assert.Equal("second (1).txt", renamed.DisplayName);
        Assert.Equal("first", File.ReadAllText(renamed.StoredPath!));
        Assert.Equal("second", File.ReadAllText(second.StoredPath!));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("bad:name.txt")]
    [InlineData("CON.txt")]
    [InlineData("LPT1")]
    [InlineData("ending.")]
    [InlineData(" ")]
    public async Task Rename_InvalidNamePreservesOriginal(string name)
    {
        using var w = await Workspace.CreateAsync();
        var item = await w.Service.ImportPathAsync(w.Box.Id, w.Source("safe", "safe"));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Service.RenameItemAsync(w.Box.Id, item.Id, name));
        Assert.Equal(item.StoredPath, (await w.Repository.GetItemAsync(item.Id))!.StoredPath);
        Assert.Equal("safe", File.ReadAllText(item.StoredPath!));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task RenameMappingReference_ChangesOnlyAliasEvenIfSourceIsMissing()
    {
        using var w = await Workspace.CreateAsync();
        var box = await w.Service.CreateBoxAsync("mapped", BoxType.Mapping);
        var source = w.Source("mapped", "source");
        var item = await w.Service.ImportPathAsync(box.Id, source);
        var renamed = await w.Service.RenameItemAsync(box.Id, item.Id, "alias.txt");
        Assert.Equal(source, renamed.SourcePath);
        Assert.Null(renamed.StoredPath);
        Assert.Equal("source", File.ReadAllText(source));
        File.Delete(source);
        await w.Service.RenameItemAsync(box.Id, item.Id, "missing-alias.txt");
        Assert.Equal("missing-alias.txt", (await w.Repository.GetItemAsync(item.Id))!.DisplayName);
    }

    [Fact]
    public async Task StaleBoxActions_RejectMovedItem()
    {
        using var w = await Workspace.CreateAsync();
        var item = await w.Service.ImportPathAsync(w.Box.Id, w.Source("moved", "payload"));
        var second = await w.Service.CreateBoxAsync("second", BoxType.Normal);
        await w.Service.MoveItemToBoxAsync(item.Id, second.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.RenameItemAsync(w.Box.Id, item.Id, "wrong.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.DeleteItemFromBoxAsync(w.Box.Id, item.Id));
        var current = await w.Repository.GetItemAsync(item.Id);
        Assert.Equal(second.Id, current!.BoxId);
        Assert.Equal("payload", File.ReadAllText(current.StoredPath!));
    }

    [Fact]
    public async Task Copy_RecordFailurePreservesBothFilesAndRecoversOnRestart()
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("copied", "payload");
        await w.Sql("CREATE TRIGGER FailCopy BEFORE INSERT ON Items BEGIN SELECT RAISE(ABORT, 'fixture'); END;");
        await Assert.ThrowsAsync<IOException>(() => w.Service.CopyPathToBoxAsync(w.Box.Id, source));
        var pending = Assert.Single(await w.Repository.GetPendingFileOperationsAsync());
        Assert.Equal(PendingFileOperationKind.Copy, pending.Kind);
        Assert.Equal("payload", File.ReadAllText(source));
        Assert.Equal("payload", File.ReadAllText(pending.TargetPath));
        await w.Sql("DROP TRIGGER FailCopy;");
        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();
        Assert.Equal(pending.TargetPath, (await w.Repository.GetItemAsync(pending.ItemId))!.StoredPath);
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
        Assert.Empty(restarted.RecoveryWarnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CopyRecovery_PromotedCopyCommitsEvenIfSourceIsNowMissing(bool sourceExists)
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("recovery", "payload");
        var target = Path.Combine(w.Box.StoragePath!, "copy.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), w.Box.Id, "copy.txt", ItemKind.File, source, target, 0, now, now);
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Copy, item.Id, source, target, false, item);
        await w.Repository.AddPendingFileOperationAsync(operation);
        File.Copy(source, target);
        if (!sourceExists) File.Delete(source);
        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();
        Assert.NotNull(await w.Repository.GetItemAsync(item.Id));
        Assert.Equal("payload", File.ReadAllText(target));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameRecovery_CaseOnlyStagingFinishesRename(bool directory)
    {
        using var w = await Workspace.CreateAsync();
        var item = await w.Service.ImportPathAsync(w.Box.Id, w.Source("Name", "payload", directory));
        var target = Path.Combine(w.Box.StoragePath!, directory ? "NAME" : "NAME.txt");
        var result = item with { StoredPath = target, DisplayName = Path.GetFileName(target) };
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Rename,
            item.Id, item.StoredPath!, target, directory, result);
        await w.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        if (directory) Directory.Move(item.StoredPath!, stage);
        else File.Move(item.StoredPath!, stage);
        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();
        Assert.Equal(Path.GetFileName(target), (await w.Repository.GetItemAsync(item.Id))!.DisplayName);
        Assert.Equal("payload", w.Read(target, directory));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CopyRecovery_PartialStagingUsesSourceAvailability(bool sourceExists)
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("partial", "complete source");
        var target = Path.Combine(w.Box.StoragePath!, "copy.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), w.Box.Id, "copy.txt", ItemKind.File, source, target, 0, now, now);
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Copy,
            item.Id, source, target, false, item);
        await w.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        File.WriteAllText(stage, "partial");
        if (!sourceExists) File.Delete(source);

        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();

        Assert.Null(await w.Repository.GetItemAsync(item.Id));
        Assert.False(File.Exists(target));
        if (sourceExists)
        {
            Assert.Equal("complete source", File.ReadAllText(source));
            Assert.False(File.Exists(stage));
            Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
        }
        else
        {
            Assert.Equal("partial", File.ReadAllText(stage));
            Assert.Single(await w.Repository.GetPendingFileOperationsAsync());
            Assert.NotEmpty(restarted.RecoveryWarnings);
        }
    }

    [Fact]
    public async Task CopyRecovery_StagingAndOccupiedTargetPreserveBothForInspection()
    {
        using var w = await Workspace.CreateAsync();
        var source = w.Source("conflict", "source");
        var target = Path.Combine(w.Box.StoragePath!, "copy.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), w.Box.Id, "copy.txt", ItemKind.File, source, target, 0, now, now);
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Copy,
            item.Id, source, target, false, item);
        await w.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        File.WriteAllText(stage, "partial");
        File.WriteAllText(target, "other process");

        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();

        Assert.Null(await w.Repository.GetItemAsync(item.Id));
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("partial", File.ReadAllText(stage));
        Assert.Equal("other process", File.ReadAllText(target));
        Assert.Single(await w.Repository.GetPendingFileOperationsAsync());
        Assert.NotEmpty(restarted.RecoveryWarnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rename_RecordFailurePreservesFileAndRecovers(bool caseOnly)
    {
        using var w = await Workspace.CreateAsync();
        var item = await w.Service.ImportPathAsync(w.Box.Id, w.Source("original", "payload"));
        var name = caseOnly ? "ORIGINAL.txt" : "renamed.txt";
        await w.Sql("CREATE TRIGGER FailRename BEFORE UPDATE ON Items BEGIN SELECT RAISE(ABORT, 'fixture'); END;");

        await Assert.ThrowsAsync<IOException>(() => w.Service.RenameItemAsync(w.Box.Id, item.Id, name));

        Assert.Equal("payload", File.ReadAllText(item.StoredPath!));
        Assert.Equal(item.StoredPath, (await w.Repository.GetItemAsync(item.Id))!.StoredPath);
        await w.Sql("DROP TRIGGER FailRename;");
        var restarted = new DrawerService(w.Paths, w.Repository);
        await restarted.InitializeAsync();
        var current = (await w.Repository.GetItemAsync(item.Id))!;
        Assert.Equal(caseOnly ? name : item.DisplayName, current.DisplayName);
        Assert.Equal("payload", File.ReadAllText(current.StoredPath!));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
        Assert.Empty(restarted.RecoveryWarnings);
    }

    [Fact]
    public async Task Copy_RejectsInternalPathsAndHonorsCancellation()
    {
        using var w = await Workspace.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.CopyPathToBoxAsync(w.Box.Id, w.Paths.DatabasePath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.CopyPathToBoxAsync(w.Box.Id, w.Box.StoragePath!));
        var source = w.Source("canceled", "preserved");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.Service.CopyPathToBoxAsync(w.Box.Id, source, new CancellationToken(true)));
        Assert.Equal("preserved", File.ReadAllText(source));
        Assert.Empty(await w.Service.GetItemsAsync(w.Box.Id));
        Assert.Empty(await w.Repository.GetPendingFileOperationsAsync());
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "WitchDrawer.FileCommands", Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public DrawerRepository Repository { get; }
        public DrawerService Service { get; }
        public Box Box { get; private set; } = null!;
        private Workspace()
        {
            Paths = new AppPaths(Path.Combine(Root, "data"));
            Repository = new DrawerRepository(Paths.DatabasePath);
            Service = new DrawerService(Paths, Repository);
        }
        public static async Task<Workspace> CreateAsync()
        {
            var w = new Workspace(); await w.Service.InitializeAsync();
            w.Box = await w.Service.CreateBoxAsync("files", BoxType.Normal); return w;
        }
        public string Source(string name, string content, bool directory = false)
        {
            var parent = Path.Combine(Root, "sources"); Directory.CreateDirectory(parent);
            var path = Path.Combine(parent, name + (directory ? "" : ".txt"));
            if (directory) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "nested.txt"), content); }
            else File.WriteAllText(path, content);
            return path;
        }
        public string Read(string path, bool directory) => File.ReadAllText(directory ? Path.Combine(path, "nested.txt") : path);
        public async Task Sql(string sql)
        {
            await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString());
            await c.OpenAsync(); using var command = c.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
