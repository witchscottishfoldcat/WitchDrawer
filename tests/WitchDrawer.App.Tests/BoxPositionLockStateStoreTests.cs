using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class BoxPositionLockStateStoreTests
{
    [Fact]
    public async Task LoadAsync_UsesUnlockedStateWithoutSavedPreference()
    {
        await using var workspace = await TestWorkspace.CreateAsync();

        var isPositionLocked = await workspace.Store.LoadAsync(Guid.NewGuid());

        Assert.False(isPositionLocked);
    }

    [Fact]
    public async Task SaveAsync_PersistsPositionLockState()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var boxId = Guid.NewGuid();

        await workspace.Store.SaveAsync(boxId, isPositionLocked: true);

        var reloadedStore = new BoxPositionLockStateStore(workspace.DrawerService, workspace.Logger);
        Assert.True(await reloadedStore.LoadAsync(boxId));
    }

    [Fact]
    public async Task LoadAsync_InvalidSavedValueFallsBackWithoutThrowing()
    {
        await using var workspace = await TestWorkspace.CreateAsync();
        var boxId = Guid.NewGuid();
        await workspace.DrawerService.SetSettingAsync(
            BoxPositionLockStateStore.GetSettingKey(boxId),
            "not-a-boolean");

        var isPositionLocked = await workspace.Store.LoadAsync(boxId);

        Assert.False(isPositionLocked);
        Assert.Single(workspace.Logger.Errors);
    }

    private sealed class TestWorkspace : IAsyncDisposable
    {
        private TestWorkspace(
            string root,
            DrawerService drawerService,
            RecordingLogger logger,
            BoxPositionLockStateStore store)
        {
            Root = root;
            DrawerService = drawerService;
            Logger = logger;
            Store = store;
        }

        public string Root { get; }

        public DrawerService DrawerService { get; }

        public RecordingLogger Logger { get; }

        public BoxPositionLockStateStore Store { get; }

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
                new BoxPositionLockStateStore(drawerService, logger));
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
