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
    public async Task ImportPathAsync_PersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "grid.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source, 2, 3);
        var storedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.Equal(2, item.GridColumn);
        Assert.Equal(3, item.GridRow);
        Assert.NotNull(storedItem);
        Assert.Equal(2, storedItem.GridColumn);
        Assert.Equal(3, storedItem.GridRow);
    }

    [Fact]
    public async Task UpdateItemGridPositionAsync_PersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reposition.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source, 0, 0);

        await workspace.Service.UpdateItemGridPositionAsync(item.Id, 4, 5);
        var storedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.NotNull(storedItem);
        Assert.Equal(4, storedItem.GridColumn);
        Assert.Equal(5, storedItem.GridRow);
    }

    [Fact]
    public async Task SetSettingAsync_PersistsAndUpdatesValue()
    {
        using var workspace = await TestWorkspace.CreateAsync();

        await workspace.Service.SetSettingAsync("Theme", "Moe");
        await workspace.Service.SetSettingAsync("Theme", "Crystal");
        var value = await workspace.Service.GetSettingAsync("Theme");

        Assert.Equal("Crystal", value);
    }

    [Fact]
    public async Task ImportPathAsync_PixelBoxMovesFileIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-p", "pixelart.png", "hello");
        var pixelBox = await workspace.Service.CreateBoxAsync("像素收纳盒 1", BoxType.Pixel);

        var item = await workspace.Service.ImportPathAsync(pixelBox.Id, source);
        var storedItems = await workspace.Repository.GetItemsAsync(pixelBox.Id);

        Assert.False(File.Exists(source));
        Assert.NotNull(item.StoredPath);
        Assert.True(File.Exists(item.StoredPath));
        Assert.Equal(source, item.SourcePath);
        Assert.Equal("pixelart.png", item.DisplayName);
        Assert.Single(storedItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_PixelBoxUsesTrashAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-p", "boxedpixel.txt", "hello");
        var pixelBox = await workspace.Service.CreateBoxAsync("像素收纳盒 1", BoxType.Pixel);
        await workspace.Service.ImportPathAsync(pixelBox.Id, source);
        var trash = new RecordingTrash();

        await workspace.Service.DeleteBoxAsync(pixelBox.Id, trash);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(pixelBox.Id);

        Assert.Equal(pixelBox.StoragePath, Assert.Single(trash.Paths));
        Assert.DoesNotContain(boxes, box => box.Id == pixelBox.Id);
        Assert.Empty(remainingItems);
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
    public async Task MoveItemToBoxAsync_NormalBoxMovesStoredFileAndPersistsGridPosition()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "move-me.txt", "hello");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, source, 0, 0);
        var oldStoredPath = item.StoredPath!;

        await workspace.Service.MoveItemToBoxAsync(item.Id, targetBox.Id, 2, 3);
        var movedItem = await workspace.Repository.GetItemAsync(item.Id);
        var sourceItems = await workspace.Repository.GetItemsAsync(sourceBox.Id);

        Assert.NotNull(movedItem);
        Assert.Equal(targetBox.Id, movedItem.BoxId);
        Assert.Equal(source, movedItem.SourcePath);
        Assert.Equal("move-me.txt", movedItem.DisplayName);
        Assert.Equal(2, movedItem.GridColumn);
        Assert.Equal(3, movedItem.GridRow);
        Assert.False(File.Exists(oldStoredPath));
        Assert.NotNull(movedItem.StoredPath);
        Assert.True(File.Exists(movedItem.StoredPath));
        Assert.Empty(sourceItems);
        Assert.StartsWith(Path.GetFullPath(targetBox.StoragePath!), Path.GetFullPath(movedItem.StoredPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveItemToBoxAsync_NormalBoxAddsSuffixForConflictingTargetName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var first = workspace.CreateSourceFile("source-a", "report.txt", "one");
        var second = workspace.CreateSourceFile("source-b", "report.txt", "two");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);

        var existingItem = await workspace.Service.ImportPathAsync(targetBox.Id, first);
        var movingItem = await workspace.Service.ImportPathAsync(sourceBox.Id, second);

        await workspace.Service.MoveItemToBoxAsync(movingItem.Id, targetBox.Id, 1, 1);
        var movedItem = await workspace.Repository.GetItemAsync(movingItem.Id);

        Assert.NotNull(movedItem);
        Assert.Equal("report.txt", existingItem.DisplayName);
        Assert.Equal("report (1).txt", movedItem.DisplayName);
        Assert.True(File.Exists(existingItem.StoredPath));
        Assert.NotNull(movedItem.StoredPath);
        Assert.True(File.Exists(movedItem.StoredPath));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_StoredItemToMappingBoxIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "stored.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MoveItemToBoxAsync(item.Id, mappingBox.Id, 1, 1));

        var storedItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(storedItem);
        Assert.Equal(normalBox.Id, storedItem.BoxId);
        Assert.True(File.Exists(item.StoredPath));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_MappingBoxMovesReferenceWithoutTouchingSourceFile()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var sourceBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var targetBox = await workspace.Service.CreateBoxAsync("target-map", BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, source, 0, 0);

        await workspace.Service.MoveItemToBoxAsync(item.Id, targetBox.Id, 2, 4);
        var movedItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.NotNull(movedItem);
        Assert.Equal(targetBox.Id, movedItem.BoxId);
        Assert.Equal(source, movedItem.SourcePath);
        Assert.Null(movedItem.StoredPath);
        Assert.Equal(2, movedItem.GridColumn);
        Assert.Equal(4, movedItem.GridRow);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MoveItemToBoxAsync_MappingItemToStorageBoxIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MoveItemToBoxAsync(item.Id, normalBox.Id, 1, 1));

        var storedItem = await workspace.Repository.GetItemAsync(item.Id);
        Assert.NotNull(storedItem);
        Assert.Equal(mappingBox.Id, storedItem.BoxId);
        Assert.Null(storedItem.StoredPath);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task GetItemsAsync_NormalBoxRemovesMissingStoredItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "moved-out.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var exportedPath = Path.Combine(workspace.Root, "exported", "moved-out.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(exportedPath)!);

        File.Move(item.StoredPath!, exportedPath);
        var items = await workspace.Service.GetItemsAsync(normalBox.Id);
        var storedItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(File.Exists(exportedPath));
        Assert.Empty(items);
        Assert.Empty(storedItems);
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
