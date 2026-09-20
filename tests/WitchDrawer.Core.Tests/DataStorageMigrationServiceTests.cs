using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DataStorageMigrationServiceTests
{
    [Fact]
    public async Task MigrateAsync_CopiesDataAndWritesBootstrapConfig()
    {
        var sourceRoot = CreateTempDirectory();
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(sourceRoot);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var boxFile = Path.Combine(paths.BoxesDirectory, "box-file.txt");
            await File.WriteAllTextAsync(boxFile, "stored item");
            await File.WriteAllTextAsync(
                Path.Combine(paths.BoxesDirectory, StorageLocationStore.ConfigFileName),
                "user file");
            var logFile = Path.Combine(paths.LogsDirectory, "app.log");
            await File.WriteAllTextAsync(logFile, "log");
            // 引导配置只应留在引导目录，不随数据复制。
            await File.WriteAllTextAsync(
                Path.Combine(paths.RootDirectory, StorageLocationStore.ConfigFileName),
                "{}");
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            var migration = new DataStorageMigrationService(paths, repository, store);

            var newPaths = await migration.MigrateAsync(targetRoot);

            Assert.Equal(Path.GetFullPath(targetRoot), newPaths.RootDirectory);
            Assert.True(File.Exists(newPaths.DatabasePath));
            Assert.Equal(
                "stored item",
                await File.ReadAllTextAsync(Path.Combine(newPaths.BoxesDirectory, "box-file.txt")));
            Assert.Equal(
                "user file",
                await File.ReadAllTextAsync(Path.Combine(
                    newPaths.BoxesDirectory, StorageLocationStore.ConfigFileName)));
            Assert.Equal(
                "log",
                await File.ReadAllTextAsync(Path.Combine(newPaths.LogsDirectory, "app.log")));
            Assert.False(File.Exists(
                Path.Combine(newPaths.RootDirectory, StorageLocationStore.ConfigFileName)));
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());

            // 迁移后可从新目录重新打开数据库。
            var reopened = new DrawerService(newPaths, new DrawerRepository(newPaths.DatabasePath));
            await reopened.InitializeAsync();
            Assert.NotEmpty(await reopened.GetBoxesAsync());
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task MigrateAsync_SourceNameMatchingLegacyTempSuffix_DoesNotDeleteSource()
    {
        var parent = CreateTempDirectory();
        var sourceRoot = Path.Combine(parent, "data.tmp-migrating");
        var targetRoot = Path.Combine(parent, "data");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            var migration = await CreateMigrationAsync(sourceRoot);
            var sentinel = Path.Combine(sourceRoot, "sentinel.txt");
            await File.WriteAllTextAsync(sentinel, "keep");

            await migration.MigrateAsync(targetRoot);

            Assert.True(File.Exists(sentinel));
            Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task MigrateAsync_ReopenedServiceRepairsStoredPathsAndCanExportItem()
    {
        var sourceRoot = CreateTempDirectory();
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        var exportRoot = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(sourceRoot);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var normalBox = (await service.GetBoxesAsync()).Single(
                box => box.Type == WitchDrawer.Core.Models.BoxType.Normal);
            var sourceFile = Path.Combine(sourceRoot, "source.txt");
            await File.WriteAllTextAsync(sourceFile, "payload");
            var item = await service.ImportPathAsync(normalBox.Id, sourceFile);

            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            var migration = new DataStorageMigrationService(paths, repository, store);
            var newPaths = await migration.MigrateAsync(targetRoot);
            var reopened = new DrawerService(newPaths, new DrawerRepository(newPaths.DatabasePath));
            await reopened.InitializeAsync();

            var exportedPath = await reopened.ExportItemToDirectoryAsync(item.Id, exportRoot);

            Assert.Equal("payload", await File.ReadAllTextAsync(exportedPath));
            Assert.StartsWith(Path.GetFullPath(exportRoot), exportedPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
            DeleteDirectory(exportRoot);
        }
    }

    [Fact]
    public async Task MigrateAsync_RejectsNonEmptyTarget()
    {
        var sourceRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var migration = await CreateMigrationAsync(sourceRoot);
            await File.WriteAllTextAsync(Path.Combine(targetRoot, "existing.txt"), "keep");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => migration.MigrateAsync(targetRoot));

            Assert.Contains("不为空", exception.Message);
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task MigrateAsync_RejectsPendingFileMoveAndKeepsOriginalDataActive()
    {
        var sourceRoot = CreateTempDirectory();
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(sourceRoot);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = (await service.GetBoxesAsync()).Single(value => value.Type == BoxType.Normal);
            var source = Path.Combine(sourceRoot, "source.txt");
            await File.WriteAllTextAsync(source, "payload");
            var item = await service.ImportPathAsync(box.Id, source);
            var exportPath = Path.Combine(sourceRoot, "export.txt");
            await repository.AddPendingFileOperationAsync(new PendingFileOperation(
                Guid.NewGuid(), PendingFileOperationKind.Remove, item.Id,
                item.StoredPath!, exportPath, false, null));
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            var migration = new DataStorageMigrationService(paths, repository, store);

            await Assert.ThrowsAsync<InvalidOperationException>(() => migration.MigrateAsync(targetRoot));

            Assert.True(File.Exists(item.StoredPath));
            Assert.NotNull(await repository.GetItemAsync(item.Id));
            Assert.Null(store.LoadConfiguredDirectory());
            Assert.False(Directory.Exists(targetRoot));
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task DatabaseSnapshot_RejectsWriteThatCompletesDuringFileCopy()
    {
        var sourceRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var repository = new DrawerRepository(new AppPaths(sourceRoot).DatabasePath);
            await repository.InitializeAsync();
            var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var targetDatabasePath = Path.Combine(targetRoot, AppPaths.DatabaseFileName);
            var snapshotTask = repository.CopyConsistentDatabaseAsync(
                targetDatabasePath,
                () => Task.CompletedTask,
                async () =>
                {
                    copying.TrySetResult();
                    await releaseCopy.Task;
                },
                () => Task.CompletedTask);

            await copying.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await repository.SetSettingAsync("during-copy", "new-value");
            try
            {
                Assert.Equal("new-value", await repository.GetSettingAsync("during-copy"));
            }
            finally
            {
                releaseCopy.TrySetResult();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => snapshotTask.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.False(File.Exists(targetDatabasePath));
            Assert.Equal("new-value", await repository.GetSettingAsync("during-copy"));
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task DatabaseSnapshot_WriterFailsFastDuringShortFinalizationLock()
    {
        var sourceRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var repository = new DrawerRepository(new AppPaths(sourceRoot).DatabasePath);
            await repository.InitializeAsync();
            var finalizing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFinalization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var snapshotTask = repository.CopyConsistentDatabaseAsync(
                Path.Combine(targetRoot, AppPaths.DatabaseFileName),
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                async () =>
                {
                    finalizing.TrySetResult();
                    await releaseFinalization.Task;
                });

            await finalizing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.SetSettingAsync("during-finalize", "value")
                        .WaitAsync(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                releaseFinalization.TrySetResult();
            }

            await snapshotTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(await repository.GetSettingAsync("during-finalize"));
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public void LoadConfiguredDirectory_PromotesCompletedMigrationIntent()
    {
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            var migrationId = Guid.NewGuid();
            store.SaveMigrationIntent(targetRoot, migrationId);
            File.WriteAllText(Path.Combine(targetRoot, AppPaths.DatabaseFileName), "db");
            File.WriteAllText(
                Path.Combine(targetRoot, StorageLocationStore.MigrationMarkerFileName),
                migrationId.ToString("N"));

            // 提升完成但引导配置未写完时崩溃：下次启动必须根据意图+标记完成切换。
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
            Assert.False(File.Exists(store.FilePath + ".migration"));
            Assert.False(File.Exists(
                Path.Combine(targetRoot, StorageLocationStore.MigrationMarkerFileName)));
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public void LoadConfiguredDirectory_IgnoresIntentWhenMarkerDoesNotMatch()
    {
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            store.SaveMigrationIntent(targetRoot, Guid.NewGuid());
            File.WriteAllText(Path.Combine(targetRoot, AppPaths.DatabaseFileName), "db");
            // 标记与意图不匹配：可能是另一次迁移的残留，绝不能切换过去。
            File.WriteAllText(
                Path.Combine(targetRoot, StorageLocationStore.MigrationMarkerFileName),
                Guid.NewGuid().ToString("N"));

            Assert.Null(store.LoadConfiguredDirectory());
            Assert.True(File.Exists(store.FilePath + ".migration"));
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public void PromoteStagedDirectory_TargetChangedAfterValidation_PreservesForeignFiles()
    {
        var parent = CreateTempDirectory();
        var stagingRoot = Path.Combine(parent, "target.tmp-migrating-test");
        var targetRoot = Path.Combine(parent, "target");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(targetRoot);
        var stagedFile = Path.Combine(stagingRoot, "copied.txt");
        var foreignFile = Path.Combine(targetRoot, "late-arrival.txt");
        File.WriteAllText(stagedFile, "copied");
        File.WriteAllText(foreignFile, "must-survive");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => DataStorageMigrationService.PromoteStagedDirectory(stagingRoot, targetRoot));

            Assert.Contains("preserved", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(foreignFile));
            Assert.Equal("must-survive", File.ReadAllText(foreignFile));
            Assert.True(File.Exists(stagedFile));
            Assert.Equal("copied", File.ReadAllText(stagedFile));
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task MigrateAsync_RejectsSameOrNestedTarget()
    {
        var sourceRoot = CreateTempDirectory();
        try
        {
            var migration = await CreateMigrationAsync(sourceRoot);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => migration.MigrateAsync(sourceRoot));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => migration.MigrateAsync(Path.Combine(sourceRoot, "nested")));
        }
        finally
        {
            DeleteDirectory(sourceRoot);
        }
    }

    [Fact]
    public void StorageLocationStore_SaveAndLoadRoundTrips()
    {
        var bootstrapRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));

            Assert.Null(store.LoadConfiguredDirectory());

            store.SaveConfiguredDirectory(@"D:\Data\WitchDrawer");

            Assert.Equal(
                Path.GetFullPath(@"D:\Data\WitchDrawer"),
                store.LoadConfiguredDirectory());
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
        }
    }

    [Fact]
    public void StorageLocationStore_CompletesPromotedMigrationAfterInterruption()
    {
        var bootstrapRoot = CreateTempDirectory();
        var oldRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            store.SaveConfiguredDirectory(oldRoot);
            var migrationId = Guid.NewGuid();
            store.SaveMigrationIntent(targetRoot, migrationId);
            File.WriteAllText(Path.Combine(targetRoot, AppPaths.DatabaseFileName), "snapshot");
            File.WriteAllText(
                Path.Combine(targetRoot, StorageLocationStore.MigrationMarkerFileName),
                migrationId.ToString("N"));

            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
            Assert.False(File.Exists(store.FilePath + ".migration"));
            Assert.False(File.Exists(
                Path.Combine(targetRoot, StorageLocationStore.MigrationMarkerFileName)));
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(oldRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public void StorageLocationStore_IncompleteMigrationKeepsOldDirectory()
    {
        var bootstrapRoot = CreateTempDirectory();
        var oldRoot = CreateTempDirectory();
        var targetRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            store.SaveConfiguredDirectory(oldRoot);
            store.SaveMigrationIntent(targetRoot, Guid.NewGuid());

            Assert.Equal(Path.GetFullPath(oldRoot), store.LoadConfiguredDirectory());
            Assert.False(File.Exists(Path.Combine(targetRoot, AppPaths.DatabaseFileName)));
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(oldRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task MigrateAsync_CopyFailureCleansUpAndAllowsRetry()
    {
        var sourceRoot = CreateTempDirectory();
        var targetRoot = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        var tempRoot = targetRoot + ".tmp-migrating";
        try
        {
            var paths = new AppPaths(sourceRoot);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var store = new StorageLocationStore(
                Path.Combine(CreateTempDirectory(), StorageLocationStore.ConfigFileName));
            var migration = new DataStorageMigrationService(paths, repository, store);

            // 锁定一个排在数据库之后的源文件，让复制在中途失败（此时 db 已复制进临时目录）。
            var lockedFile = Path.Combine(sourceRoot, "zzz-locked.txt");
            await File.WriteAllTextAsync(lockedFile, "locked");
            using (var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAnyAsync<IOException>(() => migration.MigrateAsync(targetRoot));

                // 失败只残留可自动清理的临时目录：临时目录被清除，目标目录不含半成品数据。
                Assert.False(Directory.Exists(tempRoot));
                Assert.True(
                    !Directory.Exists(targetRoot)
                    || !Directory.EnumerateFileSystemEntries(targetRoot).Any());
                Assert.Null(store.LoadConfiguredDirectory());
            }

            // 释放文件锁后可直接重试（目标空目录存在也不应被拒）。
            var newPaths = await migration.MigrateAsync(targetRoot);

            Assert.True(File.Exists(newPaths.DatabasePath));
            Assert.False(Directory.Exists(tempRoot));
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(targetRoot);
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StorageLocationStore_SaveLeavesNoTempFileBehind()
    {
        var bootstrapRoot = CreateTempDirectory();
        try
        {
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));

            store.SaveConfiguredDirectory(@"D:\Data\WitchDrawer");

            Assert.Equal(
                Path.GetFullPath(@"D:\Data\WitchDrawer"),
                store.LoadConfiguredDirectory());
            Assert.False(File.Exists(store.FilePath + ".tmp"));
        }
        finally
        {
            DeleteDirectory(bootstrapRoot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateAsync_FreezesOldDatabaseAndUsesNewRootEvenWhenConfigIsBlocked(bool blockConfig)
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(Path.Combine(root, "old"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = (await service.GetBoxesAsync()).Single(x => x.Type == BoxType.Normal);
            var store = new StorageLocationStore(Path.Combine(root, "bootstrap", StorageLocationStore.ConfigFileName));
            store.SaveConfiguredDirectory(paths.RootDirectory);
            if (blockConfig)
            {
                Directory.CreateDirectory(store.FilePath + ".tmp");
            }
            await using var preexistingConnection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString());
            await preexistingConnection.OpenAsync();

            var target = Path.Combine(root, "new");
            var migrated = await new DataStorageMigrationService(paths, repository, store).MigrateAsync(target);
            Assert.Equal(target, store.LoadConfiguredDirectory());

            var lateSource = Path.Combine(root, "late.txt");
            File.WriteAllText(lateSource, "keep");
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => service.ImportPathAsync(box.Id, lateSource));
            Assert.Equal("keep", File.ReadAllText(lateSource));
            Assert.Empty(await repository.GetPendingFileOperationsAsync());

            var oldCommand = preexistingConnection.CreateCommand();
            oldCommand.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ('late', 'lost');";
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => oldCommand.ExecuteNonQueryAsync());
            var reopenedOld = new DrawerRepository(paths.DatabasePath);
            await reopenedOld.InitializeAsync();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => reopenedOld.SetSettingAsync("late", "lost"));

            var reopened = new DrawerService(migrated, new DrawerRepository(migrated.DatabasePath));
            await reopened.InitializeAsync();
            var imported = await reopened.ImportPathAsync(box.Id, lateSource);
            Assert.Equal("keep", File.ReadAllText(imported.StoredPath!));
            Assert.StartsWith(migrated.BoxesDirectory, imported.StoredPath!);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DatabaseSnapshot_FinalizationFailureBeforePromotionLeavesOldDatabaseWritable()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(Path.Combine(root, "old"));
            var repository = new DrawerRepository(paths.DatabasePath);
            await repository.InitializeAsync();
            await Assert.ThrowsAsync<IOException>(() => repository.CopyConsistentDatabaseAsync(
                Path.Combine(root, "snapshot.db"),
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => throw new IOException("promotion failed"),
                migrationTargetDirectory: Path.Combine(root, "new")));
            await repository.SetSettingAsync("still-writable", "yes");
            Assert.Equal("yes", await repository.GetSettingAsync("still-writable"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<DataStorageMigrationService> CreateMigrationAsync(string sourceRoot)
    {
        var paths = new AppPaths(sourceRoot);
        var repository = new DrawerRepository(paths.DatabasePath);
        var service = new DrawerService(paths, repository);
        await service.InitializeAsync();
        var store = new StorageLocationStore(
            Path.Combine(CreateTempDirectory(), StorageLocationStore.ConfigFileName));
        return new DataStorageMigrationService(paths, repository, store);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 尽力清理。
        }
    }
}
