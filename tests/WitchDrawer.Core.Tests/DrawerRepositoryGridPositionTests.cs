using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerRepositoryGridPositionTests
{
    [Fact]
    public async Task BatchGridPositionUpdate_RollsBackWhenAnyItemIsMissing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var box = await service.CreateBoxAsync("映射盒", BoxType.Mapping);
            var sourceDirectory = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "alpha.txt");
            await File.WriteAllTextAsync(sourcePath, "payload");
            var item = await service.ImportPathAsync(box.Id, sourcePath, 0, 0);

            var updates = new Dictionary<Guid, (int GridColumn, int GridRow)>
            {
                [item.Id] = (4, 3),
                [Guid.NewGuid()] = (1, 1)
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.UpdateItemGridPositionsAsync(updates));

            var persisted = Assert.Single(await service.GetItemsAsync(box.Id));
            Assert.Equal(0, persisted.GridColumn);
            Assert.Equal(0, persisted.GridRow);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
