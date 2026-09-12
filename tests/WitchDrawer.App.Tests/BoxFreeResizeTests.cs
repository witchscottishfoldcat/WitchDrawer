using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class BoxFreeResizeTests
{
    [Fact]
    public void TodoBoxResize_ClampsToBoundsAndExposesBothDimensions()
    {
        var viewModel = CreateTodoBoxViewModel(out _);

        // 默认值就是初始常量
        Assert.Equal(310, viewModel.TodoBoxWidth);
        Assert.Equal(320, viewModel.TodoBoxHeight);

        // 越界 → 钳到边界
        viewModel.ResizeTodoBox(width: 1000, height: 50);
        Assert.Equal(720, viewModel.TodoBoxWidth);
        Assert.Equal(160, viewModel.TodoBoxHeight);

        viewModel.ResizeTodoBox(width: 0, height: 9999);
        Assert.Equal(240, viewModel.TodoBoxWidth);
        Assert.Equal(720, viewModel.TodoBoxHeight);

        // 有限合法值原样接受
        viewModel.ResizeTodoBox(width: 400, height: 280);
        Assert.Equal(400, viewModel.TodoBoxWidth);
        Assert.Equal(280, viewModel.TodoBoxHeight);
    }

    [Fact]
    public void TodoBoxResize_IsIgnoredOnNonTodoBoxes()
    {
        var viewModel = CreateNormalBoxViewModel(out _);

        viewModel.ResizeTodoBox(width: 600, height: 400);

        // 非 todo 盒:属性保持默认初始值,操作被忽略
        Assert.Equal(310, viewModel.TodoBoxWidth);
        Assert.Equal(320, viewModel.TodoBoxHeight);
    }

    [Fact]
    public async Task TodoBoxSize_RoundTripsThroughPersistedSetting()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var viewModel = CreateTodoBoxViewModel(out var box, drawerService);

            viewModel.ResizeTodoBox(width: 420, height: 360);
            await viewModel.SaveTodoBoxSizeAsync();

            var reloaded = CreateTodoBoxViewModel(box, drawerService);
            await reloaded.LoadTodoBoxSizeAsync();

            Assert.Equal(420, reloaded.TodoBoxWidth);
            Assert.Equal(360, reloaded.TodoBoxHeight);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task MappingListHeight_ClampsWithinLayoutBoundsAndPersistsRoundTrip()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var viewModel = CreateMappingBoxViewModel(out var box, drawerService);

            // LayoutSettings 默认 6x6 preset:
            //   MappingListMinHeight = 58, MappingListMaxHeight = 294
            // 无覆写时取 max(Min, Max) = 294
            Assert.Equal(294, viewModel.MappingListHeight);

            viewModel.ResizeMappingListHeight(120); // 120 = 5 * 24 → 吸附后仍是 120
            Assert.Equal(120, viewModel.MappingListHeight);

            viewModel.ResizeMappingListHeight(10);   // 低于 Min → 钳到 Min
            Assert.Equal(58, viewModel.MappingListHeight);

            viewModel.ResizeMappingListHeight(5000); // 高于 Max → 钳到 Max
            Assert.Equal(294, viewModel.MappingListHeight);

            // 保存 → 重新加载保留(192 = 8 * 24,正好是行高整数倍)
            viewModel.ResizeMappingListHeight(180);
            await viewModel.SaveMappingListHeightAsync();

            var reloaded = CreateMappingBoxViewModel(box, drawerService);
            await reloaded.LoadMappingListHeightAsync();

            Assert.Equal(192, reloaded.MappingListHeight);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public void MappingListHeight_OverrideRevertsToLayoutMaxWhenCleared()
    {
        var viewModel = CreateMappingBoxViewModel(out _);

        viewModel.ResizeMappingListHeight(150); // 吸附到 6 * 24 = 144
        Assert.Equal(144, viewModel.MappingListHeight);

        viewModel.ResizeMappingListHeight(294); // 等于 layout Max(288 吸附后) → 等同清空覆写
        Assert.Equal(294, viewModel.MappingListHeight);
    }

    [Fact]
    public void FixedGridResize_RoundsPixelDeltaToIntegerCellCounts()
    {
        var viewModel = CreateNormalBoxViewModel(out _);

        // ResizeFixedGrid 在自适应模式下为 no-op,需要先切到固定模式
        viewModel.ApplySizeMode(new BoxSizeModeState(true, 6, 6));

        // 默认 6x6 preset:ItemSlotWidth=37, ItemSlotHeight=37
        // 200/37 ≈ 5.4 → round → 5
        viewModel.ResizeFixedGrid(widthDip: 200, heightDip: 200);
        Assert.Equal(5, viewModel.SizeMode.Columns);
        Assert.Equal(5, viewModel.SizeMode.Rows);
        Assert.True(viewModel.IsFixedSize);
    }

    [Fact]
    public void FixedGridResize_EnforcesOccupiedExtentAsLowerBound()
    {
        var viewModel = CreateNormalBoxViewModel(out _);

        // 固定 2x2,Items.Count=0 → 占用 1x1,降不下
        viewModel.ApplySizeMode(new BoxSizeModeState(true, 2, 2));
        viewModel.ResizeFixedGrid(widthDip: 1, heightDip: 1);
        Assert.Equal(1, viewModel.SizeMode.Columns);
        Assert.Equal(1, viewModel.SizeMode.Rows);
    }

    [Fact]
    public void FixedGridResize_NormalBoxAdaptive_AutoSwitchesToFixed()
    {
        var viewModel = CreateNormalBoxViewModel(out _);

        Assert.False(viewModel.IsFixedSize);

        // 默认 6x6 preset:ItemSlotWidth=37,500/37≈13.5→14
        viewModel.ResizeFixedGrid(widthDip: 500, heightDip: 500);

        // 角落 Thumb 现在始终可见,自适应状态下拖动会自动切到固定 m×n
        Assert.True(viewModel.IsFixedSize);
        Assert.Equal(14, viewModel.SizeMode.Columns);
        Assert.Equal(14, viewModel.SizeMode.Rows);
    }

    [Fact]
    public async Task FixedGridResize_PersistsViaSizeModeKeyAndReloads()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, _) = await CreateDrawerServiceAsync(root);
            var first = CreateNormalBoxViewModel(out var box, drawerService);

            // 进入固定模式,ResizeFixedGrid 才生效
            first.ApplySizeMode(new BoxSizeModeState(true, 6, 6));

            first.ResizeFixedGrid(widthDip: 400, heightDip: 250);
            // 400/37 ≈ 10.8 → 11,250/37 ≈ 6.8 → 7
            Assert.Equal(11, first.SizeMode.Columns);
            Assert.Equal(7, first.SizeMode.Rows);

            await first.SaveSizeModeAsync();

            var reloaded = CreateNormalBoxViewModel(box, drawerService);
            await reloaded.LoadSizeModeAsync();

            Assert.Equal(new BoxSizeModeState(true, 11, 7), reloaded.SizeMode);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public void ShowFixedResizeHandle_OnlyTrueInFixedModeForSupportingBoxes()
    {
        var normal = CreateNormalBoxViewModel(out _);
        var todo = CreateTodoBoxViewModel(out _);
        var mapping = CreateMappingBoxViewModel(out _);

        Assert.True(normal.ShowFixedResizeHandle); // 普通盒角落 Thumb 始终可见
        normal.ApplySizeMode(new BoxSizeModeState(true, 4, 4));
        Assert.True(normal.ShowFixedResizeHandle);

        Assert.False(todo.ShowFixedResizeHandle); // todo 走 TodoBoxWidth/Height,不显示固定 m×n 拇指
        Assert.True(mapping.ShowFixedResizeHandle); // 映射盒网格模式角落 Thumb 始终可见,允许自适应状态拖动自动切到固定
    }

    [Fact]
    public void FixedGridResize_OnMappingGrid_AutoSwitchesFromAdaptiveToFixed()
    {
        var viewModel = CreateMappingBoxViewModel(out _);

        Assert.False(viewModel.IsFixedSize); // 默认自适应
        Assert.True(viewModel.ShowFixedResizeHandle); // 角落 Thumb 仍可见

        // 默认 6x6 preset:ItemSlotWidth=37
        // 250/37 ≈ 6.8 → 7
        viewModel.ResizeFixedGrid(widthDip: 250, heightDip: 250);

        Assert.True(viewModel.IsFixedSize); // 拖动后自动切到固定
        Assert.Equal(7, viewModel.SizeMode.Columns);
        Assert.Equal(7, viewModel.SizeMode.Rows);
    }

    private static DesktopBoxViewModel CreateTodoBoxViewModel(
        out Box box,
        DrawerService? drawerService = null)
    {
        box = new Box(
            Guid.NewGuid(),
            "待办收纳盒",
            BoxType.Todo,
            Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N")),
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return CreateTodoBoxViewModel(box, drawerService);
    }

    private static DesktopBoxViewModel CreateTodoBoxViewModel(
        Box box,
        DrawerService? drawerService = null)
    {
        return new DesktopBoxViewModel(
            box,
            drawerService ?? new DrawerService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"))),
                new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new TodoService(new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);
    }

    private static DesktopBoxViewModel CreateNormalBoxViewModel(
        out Box box,
        DrawerService? drawerService = null)
    {
        box = new Box(
            Guid.NewGuid(),
            "普通收纳盒",
            BoxType.Normal,
            Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N")),
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return CreateNormalBoxViewModel(box, drawerService);
    }

    private static DesktopBoxViewModel CreateNormalBoxViewModel(
        Box box,
        DrawerService? drawerService = null)
    {
        return new DesktopBoxViewModel(
            box,
            drawerService ?? new DrawerService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"))),
                new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new TodoService(new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);
    }

    private static DesktopBoxViewModel CreateMappingBoxViewModel(
        out Box box,
        DrawerService? drawerService = null)
    {
        box = new Box(
            Guid.NewGuid(),
            "映射收纳盒",
            BoxType.Mapping,
            Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N")),
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return CreateMappingBoxViewModel(box, drawerService);
    }

    private static DesktopBoxViewModel CreateMappingBoxViewModel(
        Box box,
        DrawerService? drawerService = null)
    {
        return new DesktopBoxViewModel(
            box,
            drawerService ?? new DrawerService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"))),
                new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new TodoService(new DrawerRepository(Path.Combine(Path.GetTempPath(), "db.sqlite"))),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);
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
