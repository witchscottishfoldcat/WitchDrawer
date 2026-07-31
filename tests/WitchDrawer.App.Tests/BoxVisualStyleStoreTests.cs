using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class BoxVisualStyleStoreTests
{
    [Fact]
    public async Task LoadAsync_UsesModernStyleForNormalBoxWithoutSavedPreference()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Normal);

        var style = await workspace.Store.LoadAsync(box);

        Assert.Equal(BoxVisualStyle.Modern, style);
    }

    [Fact]
    public async Task LoadAsync_UsesPixelStyleForLegacyPixelBoxWithoutSavedPreference()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Pixel);

        var style = await workspace.Store.LoadAsync(box);

        Assert.Equal(BoxVisualStyle.Pixel, style);
    }

    [Fact]
    public async Task SaveAsync_PersistsPixelStyleForNormalBox()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Normal);

        await workspace.Store.SaveAsync(box.Id, BoxVisualStyle.Pixel);

        var reloadedStore = new BoxVisualStyleStore(workspace.DrawerService, workspace.Logger);
        Assert.Equal(BoxVisualStyle.Pixel, await reloadedStore.LoadAsync(box));
    }

    [Fact]
    public async Task SavedModernStyle_OverridesLegacyPixelFallback()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Pixel);

        await workspace.Store.SaveAsync(box.Id, BoxVisualStyle.Modern);

        Assert.Equal(BoxVisualStyle.Modern, await workspace.Store.LoadAsync(box));
    }

    [Fact]
    public async Task LoadAsync_InvalidSavedValueFallsBackWithoutThrowing()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Normal);
        await workspace.DrawerService.SetSettingAsync(
            BoxVisualStyleStore.GetSettingKey(box.Id),
            "unsupported-style");

        var style = await workspace.Store.LoadAsync(box);

        Assert.Equal(BoxVisualStyle.Modern, style);
        Assert.Single(workspace.Logger.Errors);
    }

    [Fact]
    public async Task LoadAsync_IgnoresSavedStyleForMappingBox()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var box = CreateBox(BoxType.Mapping);
        await workspace.DrawerService.SetSettingAsync(
            BoxVisualStyleStore.GetSettingKey(box.Id),
            BoxVisualStyle.Pixel.ToString());

        var style = await workspace.Store.LoadAsync(box);

        Assert.Equal(BoxVisualStyle.Modern, style);
    }

    private static Box CreateBox(BoxType type)
    {
        var now = DateTimeOffset.UtcNow;
        return new Box(Guid.NewGuid(), "Test", type, null, 0, now, now);
    }

    private sealed class TestWorkspace : IAsyncDisposable
    {
        private TestWorkspace(
            string root,
            DrawerService drawerService,
            RecordingLogger logger,
            BoxVisualStyleStore store)
        {
            Root = root;
            DrawerService = drawerService;
            Logger = logger;
            Store = store;
        }

        public string Root { get; }

        public DrawerService DrawerService { get; }

        public RecordingLogger Logger { get; }

        public BoxVisualStyleStore Store { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "WitchDrawerTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            return new TestWorkspace(
                root,
                drawerService,
                logger,
                new BoxVisualStyleStore(drawerService, logger));
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Information { get; } = [];

        public List<(Exception Exception, string Message)> Errors { get; } = [];

        public void Info(string message)
        {
            Information.Add(message);
        }

        public void Error(Exception exception, string message)
        {
            Errors.Add((exception, message));
        }
    }
}
