using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

// Focused coverage for the atomicity rollback added to DrawerService: when the DB write fails
// after a file has already been moved, the file must return to its source so the user neither
// loses data nor gets an orphan inside box storage.
public sealed class DrawerServiceAtomicityTests
{
    [Fact]
    public async Task ImportPathAsync_RollsBackFileWhenPersistFails()
    {
        using var workspace = await FailingAddWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("src", "doc.txt", "body");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ImportPathAsync(normalBox.Id, source));

        // The file must be back where it came from, not stranded in box storage.
        Assert.True(File.Exists(source));
        Assert.Equal("body", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_RollsBackFileWhenRemoveFails()
    {
        // Import with a healthy repository, then export through one whose RemoveItemAsync throws.
        using var setupWorkspace = await TestWorkspace.CreateAsync();
        var source = setupWorkspace.CreateSourceFile("src", "export.txt", "body");
        var normalBox = await setupWorkspace.GetBoxAsync(BoxType.Normal);
        var item = await setupWorkspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        var exportDirectory = Path.Combine(setupWorkspace.Root, "out");

        var failingService = new DrawerService(setupWorkspace.Paths, new FailingRemoveRepository(setupWorkspace.Paths.DatabasePath));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingService.ExportItemToDirectoryAsync(item.Id, exportDirectory));

        Assert.True(File.Exists(storedPath));
        Assert.False(Directory.Exists(exportDirectory) && Directory.EnumerateFileSystemEntries(exportDirectory).Any());
    }

    // Workspace whose repository throws on AddItemAsync, so the import rollback path is hit.
    private sealed class FailingAddWorkspace : IDisposable
    {
        private readonly TestWorkspace _inner;

        private FailingAddWorkspace(TestWorkspace inner)
        {
            _inner = inner;
            Root = inner.Root;
            Service = inner.Service;
        }

        public string Root { get; }
        public DrawerService Service { get; }

        public static async Task<FailingAddWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Atomic.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            paths.EnsureCreated();

            // Initialize the schema + default boxes with a healthy repository first.
            var initRepo = new DrawerRepository(paths.DatabasePath);
            var initService = new DrawerService(paths, initRepo);
            await initService.InitializeAsync();

            // Rebind the service to a repository that fails on add so the test exercises rollback.
            var failingRepo = new FailingAddRepository(paths.DatabasePath);
            var service = new DrawerService(paths, failingRepo);
            var inner = TestWorkspace.FromExisting(root, paths, failingRepo, service);
            return new FailingAddWorkspace(inner);
        }

        public string CreateSourceFile(string folderName, string fileName, string content)
            => _inner.CreateSourceFile(folderName, fileName, content);

        public async Task<Box> GetBoxAsync(BoxType type)
            => await _inner.GetBoxAsync(type);

        public void Dispose() => _inner.Dispose();
    }

    private sealed class FailingAddRepository : DrawerRepository
    {
        public FailingAddRepository(string databasePath) : base(databasePath) { }

        public override Task AddItemAsync(DrawerItem item, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated persist failure.");
    }

    private sealed class FailingRemoveRepository : DrawerRepository
    {
        public FailingRemoveRepository(string databasePath) : base(databasePath) { }

        public override Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated remove failure.");
    }
}
