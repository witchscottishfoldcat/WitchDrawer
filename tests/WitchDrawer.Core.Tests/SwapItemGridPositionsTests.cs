using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

/// <summary>
/// 映射盒「详细功能」拖拽交换的底层持久化测试：
/// SwapItemGridPositionsAsync 的双 UPDATE 单事务语义。
/// </summary>
public sealed class SwapItemGridPositionsTests
{
    [Fact]
    public async Task SwapItemGridPositions_SwapsBothItemsAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            await repository.InitializeAsync();

            var box = await CreateMappingBoxAsync(repository);
            var first = await AddItemAtAsync(repository, box.Id, "a.txt", gridColumn: 0, gridRow: 0, sortOrder: 0);
            var second = await AddItemAtAsync(repository, box.Id, "b.txt", gridColumn: 1, gridRow: 0, sortOrder: 1);

            // 契约与调用方（DesktopBoxViewModel.UpdateSwapPositionsAsync）一致：
            // 调用前内存已完成交换，这里传入的是各自的“新”坐标。
            await repository.SwapItemGridPositionsAsync(
                first.Id, second.GridColumn, second.GridRow,
                second.Id, first.GridColumn, first.GridRow);

            var items = await repository.GetItemsAsync(box.Id);
            var reloadedFirst = items.Single(item => item.Id == first.Id);
            var reloadedSecond = items.Single(item => item.Id == second.Id);

            Assert.Equal((1, 0), (reloadedFirst.GridColumn, reloadedFirst.GridRow));
            Assert.Equal((0, 0), (reloadedSecond.GridColumn, reloadedSecond.GridRow));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SwapItemGridPositions_SecondItemMissing_RollsBackFirstUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            await repository.InitializeAsync();

            var box = await CreateMappingBoxAsync(repository);
            var first = await AddItemAtAsync(repository, box.Id, "a.txt", gridColumn: 0, gridRow: 0, sortOrder: 0);
            var ghostId = Guid.NewGuid(); // 不存在的第二项：第二条 UPDATE 影响 0 行 → throw → 事务回滚

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SwapItemGridPositionsAsync(
                    first.Id, 5, 5,
                    ghostId, 0, 0));

            // 无半提交：第一项格位必须保持原值。
            var items = await repository.GetItemsAsync(box.Id);
            var reloadedFirst = items.Single(item => item.Id == first.Id);
            Assert.Equal((0, 0), (reloadedFirst.GridColumn, reloadedFirst.GridRow));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<Box> CreateMappingBoxAsync(DrawerRepository repository)
    {
        var now = DateTimeOffset.UtcNow;
        var box = new Box(
            Guid.NewGuid(),
            "映射盒",
            BoxType.Mapping,
            StoragePath: null,
            SortOrder: 0,
            CreatedAt: now,
            UpdatedAt: now);
        await repository.AddBoxAsync(box);
        return box;
    }

    private static async Task<DrawerItem> AddItemAtAsync(
        DrawerRepository repository,
        Guid boxId,
        string displayName,
        int gridColumn,
        int gridRow,
        int sortOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(),
            boxId,
            displayName,
            ItemKind.File,
            SourcePath: $@"C:\sources\{displayName}",
            StoredPath: null,
            SortOrder: sortOrder,
            CreatedAt: now,
            UpdatedAt: now,
            GridColumn: gridColumn,
            GridRow: gridRow);
        await repository.AddItemAsync(item);
        return item;
    }
}
