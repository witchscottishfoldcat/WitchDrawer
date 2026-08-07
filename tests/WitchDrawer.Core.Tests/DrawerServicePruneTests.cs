using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServicePruneTests
{
    [Fact]
    public async Task GetItemsAsync_WhenStorageRootUnavailable_KeepsItemRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.PruneTests", Guid.NewGuid().ToString("N"));
        var movedAway = false;
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();

            var box = (await service.GetBoxesAsync()).First(b => b.Type == BoxType.Normal);
            var sourceDir = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "payload");
            var item = await service.ImportPathAsync(box.Id, sourceFile);
            Assert.True(File.Exists(item.StoredPath));

            // 模拟可移动盘/网络盘掉线：整个 Boxes 目录暂时不可达。
            var offline = paths.BoxesDirectory + ".offline";
            Directory.Move(paths.BoxesDirectory, offline);
            movedAway = true;

            // “暂时看不到”不等于“已删除”：记录必须保留，文件重新挂载后可恢复。
            var items = await service.GetItemsAsync(box.Id);

            Assert.Contains(items, candidate => candidate.Id == item.Id);

            Directory.Move(offline, paths.BoxesDirectory);
            movedAway = false;
            var itemsAfterReconnect = await service.GetItemsAsync(box.Id);
            Assert.Contains(itemsAfterReconnect, candidate => candidate.Id == item.Id);
        }
        finally
        {
            if (movedAway)
            {
                var paths = new AppPaths(root);
                var offline = paths.BoxesDirectory + ".offline";
                if (Directory.Exists(offline))
                {
                    Directory.Move(offline, paths.BoxesDirectory);
                }
            }

            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
