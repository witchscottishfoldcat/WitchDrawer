using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerRepositorySettingsSnapshotTests
{
    [Fact]
    public async Task GetAllSettingsAsync_OnFreshDatabase_ReturnsEmpty()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = new DrawerRepository(new AppPaths(root).DatabasePath);
            await repository.InitializeAsync();

            var settings = await repository.GetAllSettingsAsync();

            Assert.Empty(settings);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetAllSettingsAsync_ReturnsEverySettingIncludingLegacyKeys()
    {
        var root = CreateTempRoot();
        try
        {
            var repository = new DrawerRepository(new AppPaths(root).DatabasePath);
            await repository.InitializeAsync();
            await repository.SetSettingAsync("Theme", "Glass");
            await repository.SetSettingAsync("BoxTitleVisible:abc", "False");
            await repository.SetSettingAsync("DrawerSortMode:def", "ModifiedDate");

            var settings = await repository.GetAllSettingsAsync();

            Assert.Equal(3, settings.Count);
            Assert.Equal("Glass", settings["Theme"]);
            Assert.Equal("False", settings["BoxTitleVisible:abc"]);
            Assert.Equal("ModifiedDate", settings["DrawerSortMode:def"]);

            // 快照是读取时刻的副本；之后的写入不影响已生成的快照。
            await repository.SetSettingAsync("Theme", "Moe");
            Assert.Equal("Glass", settings["Theme"]);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_RepairsStaleBoxStoragePath()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);

            var tamperedPath = Path.Combine(root, "elsewhere", box.Id.ToString("N"));
            await repository.UpdateBoxStoragePathAsync(box.Id, tamperedPath);

            await service.InitializeAsync();

            var repaired = Assert.Single(
                await repository.GetBoxesAsync(),
                candidate => candidate.Id == box.Id);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(paths.BoxesDirectory, box.Id.ToString("N"))),
                Path.GetFullPath(repaired.StoragePath!));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_RepairsStaleItemPathOnlyWhenExpectedFileExists()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "real.txt");
            await File.WriteAllTextAsync(sourceFile, "data");
            var item = await service.ImportPathAsync(box.Id, sourceFile);

            // 文件名仍在预期目录中：目录部分被篡改 → 应修复回预期路径。
            var tamperedExisting = Path.Combine(root, "elsewhere", "real.txt");
            await repository.UpdateItemStoredPathAsync(item.Id, tamperedExisting);

            // 文件名不在预期目录中：无法确认 → 保留记录不修复。
            // （第一次导入已把源文件移入收纳盒，需要重建源文件再导入。）
            await File.WriteAllTextAsync(sourceFile, "data");
            var tamperedGhost = Path.Combine(root, "elsewhere", "ghost.txt");
            var ghostItem = await service.ImportPathAsync(box.Id, sourceFile);
            await repository.UpdateItemStoredPathAsync(ghostItem.Id, tamperedGhost);

            await service.InitializeAsync();

            var items = await repository.GetItemsAsync(box.Id);
            var repaired = Assert.Single(items, candidate => candidate.Id == item.Id);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(paths.BoxesDirectory, box.Id.ToString("N"), "real.txt")),
                Path.GetFullPath(repaired.StoredPath!));
            var ghost = Assert.Single(items, candidate => candidate.Id == ghostItem.Id);
            Assert.Equal(tamperedGhost, ghost.StoredPath);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_KeepsEquivalentRecordedPathsUntouched()
    {
        var root = CreateTempRoot();
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);

            // 大小写不同的等价路径：比较优先逻辑应视为一致，不回写规范化形式。
            var caseVariant = Path.Combine(paths.BoxesDirectory, box.Id.ToString("N")).ToLowerInvariant();
            await repository.UpdateBoxStoragePathAsync(box.Id, caseVariant);

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "note.txt");
            await File.WriteAllTextAsync(sourceFile, "data");
            var item = await service.ImportPathAsync(box.Id, sourceFile);
            var expectedStored = Path.Combine(paths.BoxesDirectory, box.Id.ToString("N"), "note.txt");
            var itemCaseVariant = expectedStored.ToLowerInvariant();
            await repository.UpdateItemStoredPathAsync(item.Id, itemCaseVariant);

            await service.InitializeAsync();

            var persistedBox = Assert.Single(
                await repository.GetBoxesAsync(),
                candidate => candidate.Id == box.Id);
            Assert.Equal(caseVariant, persistedBox.StoragePath);
            var persistedItem = Assert.Single(await repository.GetItemsAsync(box.Id));
            Assert.Equal(itemCaseVariant, persistedItem.StoredPath);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
