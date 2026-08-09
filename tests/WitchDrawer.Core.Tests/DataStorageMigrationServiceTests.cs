using WitchDrawer.Core;
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
