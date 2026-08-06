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
