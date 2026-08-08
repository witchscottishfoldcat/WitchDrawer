using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 统一排序（自由/名称/大小/类型/修改日期）的显示与自由布局记忆测试。
/// </summary>
public sealed class UnifiedBoxSortTests
{
    [Fact]
    public async Task NameSort_ArrangesItemsRowFirstInNameOrder()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("普通盒", BoxType.Normal);
            // 故意以非名称序、散乱格位导入。
            var beta = CreateSourceFile(root, "beta.txt");
            var alpha = CreateSourceFile(root, "alpha.txt");
            var gamma = CreateSourceFile(root, "gamma.txt");
            await drawerService.ImportPathAsync(box.Id, beta, 3, 1);
            await drawerService.ImportPathAsync(box.Id, alpha, 0, 2);
            await drawerService.ImportPathAsync(box.Id, gamma, 1, 0);

            var viewModel = CreateViewModel(box, drawerService, repository);
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Name);
            await viewModel.LoadAsync();

            // 名称序 + 行优先（默认 wrap ≥ 4 列，单行排开）。
            Assert.Equal(
                new[] { "alpha.txt", "beta.txt", "gamma.txt" },
                viewModel.Items.Select(item => item.DisplayName).ToArray());
            Assert.Equal(
                new[] { (0, 0), (1, 0), (2, 0) },
                viewModel.Items.Select(item => (item.GridColumn, item.GridRow)).ToArray());
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SwitchingBackToFree_RestoresRememberedFreeLayout()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("普通盒", BoxType.Normal);
            var beta = CreateSourceFile(root, "beta.txt");
            var alpha = CreateSourceFile(root, "alpha.txt");
            var betaItem = await drawerService.ImportPathAsync(box.Id, beta, 3, 1);
            var alphaItem = await drawerService.ImportPathAsync(box.Id, alpha, 0, 2);

            var viewModel = CreateViewModel(box, drawerService, repository);
            await viewModel.LoadAsync();
            Assert.Equal((3, 1), (viewModel.Items.Single(i => i.Id == betaItem.Id).GridColumn,
                viewModel.Items.Single(i => i.Id == betaItem.Id).GridRow));

            // 切到名称排序：行优先重排。
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Name);
            await viewModel.LoadAsync();
            Assert.Equal(
                new[] { "alpha.txt", "beta.txt" },
                viewModel.Items.Select(item => item.DisplayName).ToArray());

            // 切回自由：精确恢复切走前的自由布局。
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Free);
            await viewModel.LoadAsync();

            Assert.Equal(
                (3, 1),
                (viewModel.Items.Single(i => i.Id == betaItem.Id).GridColumn,
                    viewModel.Items.Single(i => i.Id == betaItem.Id).GridRow));
            Assert.Equal(
                (0, 2),
                (viewModel.Items.Single(i => i.Id == alphaItem.Id).GridColumn,
                    viewModel.Items.Single(i => i.Id == alphaItem.Id).GridRow));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SortedMode_ImportWritesNoGridPosition_AndFreeLayoutStaysIntact()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("普通盒", BoxType.Normal);
            var beta = CreateSourceFile(root, "beta.txt");
            var alphaItem = await drawerService.ImportPathAsync(
                box.Id, CreateSourceFile(root, "alpha.txt"), 2, 1);
            await drawerService.ImportPathAsync(box.Id, beta, 0, 0);

            var viewModel = CreateViewModel(box, drawerService, repository);
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Name);
            await viewModel.LoadAsync();

            // 排序模式下经桌面盒导入：不写格位。
            var delta = CreateSourceFile(root, "delta.txt");
            await viewModel.ImportPathsAsync([delta]);

            var persisted = await drawerService.GetItemsAsync(box.Id);
            var deltaItem = persisted.Single(item => item.DisplayName == "delta.txt");
            Assert.True(deltaItem.GridColumn is null or < 0 || deltaItem.GridRow is null or < 0);

            // 切回自由：原有项目的自由布局不受影响，新项目分配空位。
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Free);
            await viewModel.LoadAsync();

            Assert.Equal(
                (2, 1),
                (viewModel.Items.Single(i => i.Id == alphaItem.Id).GridColumn,
                    viewModel.Items.Single(i => i.Id == alphaItem.Id).GridRow));
            var deltaSlots = viewModel.Items
                .Where(item => item.Id != deltaItem.Id)
                .Select(item => (item.GridColumn, item.GridRow))
                .ToHashSet();
            var deltaSlot = viewModel.Items.Single(i => i.Id == deltaItem.Id);
            Assert.DoesNotContain((deltaSlot.GridColumn, deltaSlot.GridRow), deltaSlots);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SortedMode_SameBoxDropDoesNotRearrange()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("普通盒", BoxType.Normal);
            var alphaItem = await drawerService.ImportPathAsync(
                box.Id, CreateSourceFile(root, "alpha.txt"), 0, 0);
            await drawerService.ImportPathAsync(box.Id, CreateSourceFile(root, "beta.txt"), 1, 0);

            var viewModel = CreateViewModel(box, drawerService, repository);
            viewModel.ApplyDrawerSortMode(DrawerItemSortMode.Name);
            await viewModel.LoadAsync();

            var before = (viewModel.Items.Single(i => i.Id == alphaItem.Id).GridColumn,
                viewModel.Items.Single(i => i.Id == alphaItem.Id).GridRow);

            var result = await viewModel.DropDrawerItemAsync(alphaItem.Id, 5, 5);

            Assert.True(result);
            var after = (viewModel.Items.Single(i => i.Id == alphaItem.Id).GridColumn,
                viewModel.Items.Single(i => i.Id == alphaItem.Id).GridRow);
            Assert.Equal(before, after);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static DesktopBoxViewModel CreateViewModel(
        Box box,
        DrawerService drawerService,
        DrawerRepository repository) =>
        new(
            box,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);

    private static string CreateSourceFile(string root, string name)
    {
        var directory = Path.Combine(root, "sources");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "payload");
        return path;
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateDrawerServiceAsync(
        string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var drawerService = new DrawerService(paths, repository);
        await drawerService.InitializeAsync();
        return (drawerService, repository);
    }

    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
