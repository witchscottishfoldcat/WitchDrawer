using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServiceTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDefaultBoxes()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        var boxes = await workspace.Service.GetBoxesAsync();

        Assert.Contains(boxes, box => box.Type == BoxType.Normal && box.Name == "普通收纳盒");
        Assert.Contains(boxes, box => box.Type == BoxType.Mapping && box.Name == "映射收纳盒");
    }

    [Fact]
    public async Task ImportPathAsync_NormalBoxMovesFileIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "report.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.False(File.Exists(source));
        Assert.NotNull(item.StoredPath);
        Assert.True(File.Exists(item.StoredPath));
        Assert.Equal(source, item.SourcePath);
        Assert.Equal("report.txt", item.DisplayName);
        Assert.Single(storedItems);
    }

    [Fact]
    public async Task ImportPathAsync_MappingBoxKeepsSourceFileInPlace()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);

        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        Assert.True(File.Exists(source));
        Assert.Equal(source, item.SourcePath);
        Assert.Null(item.StoredPath);
        Assert.Equal("reference.txt", item.DisplayName);
    }

    [Fact]
    public async Task ImportPathAsync_NormalBoxAddsSuffixForConflictingNames()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var first = workspace.CreateSourceFile("source-a", "report.txt", "one");
        var second = workspace.CreateSourceFile("source-b", "report.txt", "two");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var firstItem = await workspace.Service.ImportPathAsync(normalBox.Id, first);
        var secondItem = await workspace.Service.ImportPathAsync(normalBox.Id, second);

        Assert.Equal("report.txt", firstItem.DisplayName);
        Assert.Equal("report (1).txt", secondItem.DisplayName);
        Assert.True(File.Exists(firstItem.StoredPath));
        Assert.True(File.Exists(secondItem.StoredPath));
    }

    [Fact]
    public async Task DeleteItemAsync_NormalBoxUsesTrashAbstraction()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "delete-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var trash = new RecordingTrash();

        await workspace.Service.DeleteItemAsync(item.Id, trash);
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.Equal(item.StoredPath, Assert.Single(trash.Paths));
        Assert.True(File.Exists(item.StoredPath));
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_NormalBoxUsesTrashAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "boxed.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var trash = new RecordingTrash();

        await workspace.Service.DeleteBoxAsync(normalBox.Id, trash);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.Equal(normalBox.StoragePath, Assert.Single(trash.Paths));
        Assert.DoesNotContain(boxes, box => box.Id == normalBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_MappingBoxOnlyRemovesReferences()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        await workspace.Service.ImportPathAsync(mappingBox.Id, source);
        var trash = new RecordingTrash();

        await workspace.Service.DeleteBoxAsync(mappingBox.Id, trash);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(mappingBox.Id);

        Assert.Empty(trash.Paths);
        Assert.True(File.Exists(source));
        Assert.DoesNotContain(boxes, box => box.Id == mappingBox.Id);
        Assert.Empty(remainingItems);
    }

    private sealed class RecordingTrash : IFileTrash
    {
        public List<string> Paths { get; } = [];

        public Task MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, AppPaths paths, DrawerRepository repository, DrawerService service)
        {
            Root = root;
            Paths = paths;
            Repository = repository;
            Service = service;
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public DrawerRepository Repository { get; }

        public DrawerService Service { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);

            await service.InitializeAsync();
            return new TestWorkspace(root, paths, repository, service);
        }

        public string CreateSourceFile(string folderName, string fileName, string content)
        {
            var directory = Path.Combine(Root, "sources", folderName);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public async Task<Box> GetBoxAsync(BoxType type)
        {
            var boxes = await Service.GetBoxesAsync();
            return boxes.Single(box => box.Type == type);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup should not hide the test result.
            }
        }
    }
}
