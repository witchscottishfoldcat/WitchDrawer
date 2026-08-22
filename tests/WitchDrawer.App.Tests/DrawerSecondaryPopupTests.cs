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
/// 抽屉盒二级弹窗分区测试：封面已显示的图标不应在弹窗里重复出现。
/// </summary>
public sealed class DrawerSecondaryPopupTests
{
    [Fact]
    public async Task SecondaryPopup_ExcludesItemsAlreadyShownOnTheCover()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("抽屉盒", BoxType.Drawer);

            // 导入超过封面容量的文件，制造溢出项。
            var sourcePaths = Enumerable.Range(0, 12)
                .Select(index => CreateSourceFile(root, $"item-{index:D2}.txt"))
                .ToArray();
            foreach (var path in sourcePaths)
            {
                await drawerService.ImportPathAsync(box.Id, path);
            }

            var viewModel = CreateViewModel(box, drawerService, repository);
            // 默认封面容量 6（3 列 × 2 行），溢出时封面显示 5 个 + 展开按钮。
            await viewModel.LoadAsync();

            // 封面前 DrawerDirectItemCount 个是封面项，不应出现在弹窗里。
            var coverItemIds = viewModel.Items
                .Take(viewModel.DrawerDirectItemCount)
                .Select(item => item.Id)
                .ToHashSet();

            viewModel.SyncDrawerSecondaryFromItems();

            var popupItemIds = viewModel.DrawerSecondaryItems.Select(item => item.Id).ToHashSet();
            Assert.Empty(coverItemIds.Intersect(popupItemIds));

            // 弹窗里应当正好是封面之后的溢出项。
            var expectedOverflowIds = viewModel.Items
                .Skip(viewModel.DrawerDirectItemCount)
                .Select(item => item.Id)
                .ToArray();
            Assert.Equal(expectedOverflowIds, viewModel.DrawerSecondaryItems.Select(item => item.Id).ToArray());
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    /// <summary>
    /// 二级弹窗尺寸不变式：不滚动时可视口必须恰好容纳全部行/列，
    /// 否则最下列/最右列图标会被弹窗边缘裁掉（chrome 22 = 根 Border 1px×2 + ListBox Margin 10px×2）。
    /// </summary>
    [Theory]
    [InlineData(8, false)]
    [InlineData(12, false)]
    [InlineData(20, false)]
    [InlineData(40, false)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(20, true)]
    [InlineData(40, true)]
    public async Task SecondaryPopup_ViewportExactlyFitsContentWhenNotScrolling(
        int itemCount,
        bool showFileNames)
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("抽屉盒", BoxType.Drawer);
            var sourcePaths = Enumerable.Range(0, itemCount)
                .Select(index => CreateSourceFile(root, $"item-{index:D2}.txt"))
                .ToArray();
            foreach (var path in sourcePaths)
            {
                await drawerService.ImportPathAsync(box.Id, path);
            }

            var viewModel = CreateViewModel(box, drawerService, repository);
            await viewModel.LoadAsync();
            viewModel.ApplyFileNameVisibility(showFileNames);
            viewModel.SyncDrawerSecondaryFromItems();

            var extentHeight = viewModel.DrawerSecondaryRows * viewModel.LayoutSettings.ItemSlotHeight;
            var viewportHeight = viewModel.DrawerSecondaryPanelHeight - 22;
            if (viewModel.DrawerSecondaryHasScrollableOverflow)
            {
                Assert.True(
                    viewportHeight < extentHeight,
                    $"滚动场景可视口 {viewportHeight} 应小于内容区 {extentHeight}");
            }
            else
            {
                Assert.Equal(extentHeight, viewportHeight);
            }

            var extentWidth = viewModel.DrawerSecondaryColumns * viewModel.LayoutSettings.ItemSlotWidth;
            var viewportWidth = viewModel.DrawerSecondaryPanelWidth - 22;
            Assert.Equal(extentWidth, viewportWidth);
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
            new NoOpLogger(),
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

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
