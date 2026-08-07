using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class BoxSizeSettingsTests
{
    [Fact]
    public void SizeModeState_RoundTripsThroughSerialization()
    {
        var fixedState = new BoxSizeModeState(true, 3, 2);

        Assert.Equal("Fixed:3:2", fixedState.Serialize());
        Assert.Equal(fixedState, BoxSizeModeState.Parse(fixedState.Serialize()));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse("Adaptive"));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse(null));
        Assert.Equal(BoxSizeModeState.Adaptive, BoxSizeModeState.Parse("garbage"));
    }

    [Fact]
    public void SizeModeState_ClampsOutOfRangeValues()
    {
        var parsed = BoxSizeModeState.Parse("Fixed:99:0");

        Assert.Equal(new BoxSizeModeState(true, BoxSizeModeState.MaxColumns, BoxSizeModeState.MinCells), parsed);
    }

    [Fact]
    public void SizeModeState_FitsExtent_EnforcesLowerBound()
    {
        Assert.True(new BoxSizeModeState(true, 3, 2).FitsExtent(3, 2));
        Assert.False(new BoxSizeModeState(true, 2, 2).FitsExtent(3, 2));
        Assert.False(new BoxSizeModeState(true, 3, 1).FitsExtent(3, 2));
        Assert.True(BoxSizeModeState.Adaptive.FitsExtent(12, 8));
    }

    [Fact]
    public async Task DesktopBoxViewModel_FixedSizeKeepsViewportAutoAndClampsSlots()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = new Box(
                Guid.NewGuid(),
                "普通收纳盒",
                BoxType.Normal,
                Path.Combine(root, "box"),
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);

            Assert.False(viewModel.IsFixedSize);
            Assert.True(double.IsNaN(viewModel.GridViewportWidth));
            Assert.True(double.IsNaN(viewModel.GridViewportHeight));

            viewModel.ApplySizeMode(new BoxSizeModeState(true, 3, 2));

            // 固定模式将视口与画布硬性固定为 m×n 网格的真实物理尺寸。
            Assert.True(viewModel.IsFixedSize);
            var slotWidth = viewModel.LayoutSettings.ItemSlotWidth;
            var slotHeight = viewModel.LayoutSettings.ItemSlotHeight;
            Assert.Equal((3 * slotWidth) + 4, viewModel.GridViewportWidth);
            Assert.Equal((2 * slotHeight) + 4, viewModel.GridViewportHeight);
            Assert.Equal(3 * slotWidth, viewModel.GridCanvasWidth);
            Assert.Equal(2 * slotHeight, viewModel.GridCanvasHeight);

            var clamped = viewModel.GetGridSlot(slotWidth * 10, slotHeight * 10);
            Assert.Equal((2, 1), clamped);

            viewModel.ApplySizeMode(BoxSizeModeState.Adaptive);

            Assert.False(viewModel.IsFixedSize);
            var unclamped = viewModel.GetGridSlot(slotWidth * 10, slotHeight * 10);
            Assert.Equal((10, 10), unclamped);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task DesktopBoxViewModel_FixedSizeIsIgnoredForNonGridBoxes()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = new Box(
                Guid.NewGuid(),
                "映射收纳盒",
                BoxType.Mapping,
                null,
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);

            Assert.False(viewModel.SupportsFixedSize);

            viewModel.ApplySizeMode(new BoxSizeModeState(true, 3, 2));

            Assert.False(viewModel.IsFixedSize);
            Assert.True(double.IsNaN(viewModel.GridViewportWidth));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SizeSettingsViewModel_PersistsFixedModeAndBroadcasts()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = new Box(
                Guid.NewGuid(),
                "普通收纳盒",
                BoxType.Normal,
                Path.Combine(root, "box"),
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            var viewModel = new BoxSizeSettingsViewModel(drawerService);
            var boxViewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);
            var broadcasts = new List<BoxSizeModeChangedMessage>();
            WeakReferenceMessenger.Default.Register<BoxSizeModeChangedMessage>(
                this,
                (recipient, message) => broadcasts.Add(message));

            viewModel.SetTargetBox(boxViewModel);
            Assert.Equal(boxViewModel, viewModel.SelectedBox);
            await Task.Delay(200);

            await viewModel.UseFixedModeCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsFixedMode);
            Assert.Equal(
                "Fixed:4:4",
                await drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(box.Id)));
            var broadcast = Assert.Single(broadcasts);
            Assert.Equal((box.Id, true, 4, 4), (broadcast.BoxId, broadcast.IsFixed, broadcast.Columns, broadcast.Rows));

            await viewModel.UseAdaptiveModeCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsFixedMode);
            Assert.Equal(
                "Adaptive",
                await drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(box.Id)));
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<BoxSizeModeChangedMessage>(this);
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SizeSettingsViewModel_EnforcesOccupiedExtentAsLowerBound()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var box = new Box(
                Guid.NewGuid(),
                "普通收纳盒",
                BoxType.Normal,
                Path.Combine(root, "box"),
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            var viewModel = new BoxSizeSettingsViewModel(drawerService);
            var boxViewModel = new BoxViewModel(box, drawerService, BoxVisualStyle.Modern, false);

            viewModel.SetTargetBox(boxViewModel);
            await Task.Delay(200);

            // 桌面盒窗口上报内容实际占用 6 × 2 格。
            WeakReferenceMessenger.Default.Send(new BoxGridExtentChangedMessage(box.Id, 6, 2));

            Assert.Equal((6, 2), viewModel.SelectedExtent);

            // 切到固定模式时，列数自动抬到占用下限。
            await viewModel.UseFixedModeCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsFixedMode);
            Assert.Equal(6, viewModel.FixedColumns);
            Assert.Equal(4, viewModel.FixedRows);
            Assert.False(viewModel.CanDecreaseColumns);
            Assert.True(viewModel.CanDecreaseRows);

            // 下限校验拒绝小于占用范围的尺寸。
            await viewModel.DecreaseRowsCommand.ExecuteAsync(null);
            await viewModel.DecreaseRowsCommand.ExecuteAsync(null);

            Assert.Equal(2, viewModel.FixedRows);
            Assert.False(viewModel.CanDecreaseRows);
            Assert.Equal(
                "Fixed:6:2",
                await drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(box.Id)));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SizeSettingsViewModel_IgnoresBoxesWithoutFixedSizeSupport()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var mappingBox = new Box(
                Guid.NewGuid(), "映射", BoxType.Mapping, null, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var viewModel = new BoxSizeSettingsViewModel(drawerService);

            viewModel.SetTargetBox(
                new BoxViewModel(mappingBox, drawerService, BoxVisualStyle.Modern, false));

            Assert.Null(viewModel.SelectedBox);
            Assert.False(viewModel.HasSelection);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FixedBox_StopsImportingWhenFull()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("固定盒", BoxType.Normal);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);
            viewModel.ApplySizeMode(new BoxSizeModeState(true, 1, 2));
            await viewModel.LoadAsync();

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var files = Enumerable.Range(1, 3)
                .Select(index =>
                {
                    var path = Path.Combine(sourceDir, $"file{index}.txt");
                    File.WriteAllText(path, "payload");
                    return path;
                })
                .ToArray();

            var importedIds = await viewModel.ImportPathsAsync(files, null, null);

            // 硬约束：1×2 只能装 2 项，第 3 个文件保持原样。
            Assert.Equal(2, importedIds.Count);
            Assert.Equal(2, viewModel.Items.Count);
            Assert.Equal("已收纳 2 项，盒子已满", viewModel.StatusText);
            Assert.True(File.Exists(Path.Combine(sourceDir, "file3.txt")));

            Assert.False(viewModel.HasFreeSlotForDrop());
            Assert.False(viewModel.TryGetAvailableDropSlot(0, 0, null, out _));

            // 画布按内容计算；1×2 装满时恰好等于 1×2 格。
            Assert.Equal(
                viewModel.LayoutSettings.ItemSlotWidth,
                viewModel.GridCanvasWidth);
            Assert.Equal(
                2 * viewModel.LayoutSettings.ItemSlotHeight,
                viewModel.GridCanvasHeight);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FixedBox_ClampsDropSlotIntoBounds()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("固定盒", BoxType.Normal);
            var viewModel = new DesktopBoxViewModel(
                box,
                drawerService,
                new TodoService(repository),
                new NoOpFileLauncher(),
                new RecordingLogger(),
                BoxVisualStyle.Modern);
            viewModel.ApplySizeMode(new BoxSizeModeState(true, 2, 1));
            await viewModel.LoadAsync();

            var sourceDir = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceDir);
            var file = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(file, "payload");
            await viewModel.ImportPathsAsync([file]);

            // 目标格越界时钳制回边界内的空位。
            Assert.True(viewModel.TryGetAvailableDropSlot(9, 9, null, out var slot));
            Assert.Equal((1, 0), slot);
            Assert.True(viewModel.HasFreeSlotForDrop());

            // 删除一项后恢复可放入（位置来自持久化的格子坐标）。
            var item = viewModel.Items.Single();
            await viewModel.DeleteItemCommand.ExecuteAsync(item);

            Assert.Empty(viewModel.Items);
            Assert.True(viewModel.TryGetAvailableDropSlot(0, 0, null, out var freedSlot));
            Assert.Equal((0, 0), freedSlot);
        }
        finally
        {
            CleanupTempRoot(root);
        }
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
        // 构造期 fire-and-forget 的异步读取可能仍持有 SQLite 文件句柄，重试等待释放。
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
