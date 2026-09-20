using WitchDrawer.Core;
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
    public async Task ReorderBoxesAsync_PersistsCompleteOrder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        await workspace.Service.CreateBoxAsync("third", BoxType.Normal);
        var original = await workspace.Service.GetBoxesAsync();
        var expectedIds = original.Select(box => box.Id).Reverse().ToArray();

        await workspace.Service.ReorderBoxesAsync(expectedIds);

        var reordered = await workspace.Service.GetBoxesAsync();
        Assert.Equal(expectedIds, reordered.Select(box => box.Id));
        Assert.Equal(Enumerable.Range(0, expectedIds.Length), reordered.Select(box => box.SortOrder));
    }

    [Fact]
    public async Task ReorderBoxesAsync_RejectsIncompleteOrDuplicateOrder()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var boxes = await workspace.Service.GetBoxesAsync();
        var originalIds = boxes.Select(box => box.Id).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.ReorderBoxesAsync([originalIds[0], originalIds[0]]));
        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.ReorderBoxesAsync([originalIds[0]]));

        Assert.Equal(originalIds, (await workspace.Service.GetBoxesAsync()).Select(box => box.Id));
    }

    [Fact]
    public void EnsureCreatedAndWritable_SucceedsOnWritableDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreatedAndWritable();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.BoxesDirectory));
            Assert.True(Directory.Exists(paths.LogsDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ForCurrentUser_UsesEnvironmentOverrideWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName, root);

            var paths = AppPaths.ForCurrentUser();

            Assert.Equal(Path.GetFullPath(root), paths.RootDirectory);
            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.Equal(Path.Combine(paths.RootDirectory, AppPaths.DatabaseFileName), paths.DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName, previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenDatabaseDirectoryIsNotWritable_ThrowsWithPathContext()
    {
        // 将“目录”做成文件，使 CreateDirectory / SQLite 打开必然失败。
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "not-a-directory");
        var blockedDatabasePath = Path.Combine(root, AppPaths.DatabaseFileName);

        try
        {
            var repository = new DrawerRepository(blockedDatabasePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.InitializeAsync());

            Assert.Contains(AppPaths.DataDirectoryEnvironmentVariableName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(blockedDatabasePath, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
        }
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
    public async Task ImportPathAsync_DirectoryContainsLockedFile_PreservesSourceAndAddsNoRecord()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceDirectory = workspace.CreateSourceDirectory("locked-source", "a.txt", "ordinary");
        var lockedFile = Path.Combine(sourceDirectory, "locked.txt");
        File.WriteAllText(lockedFile, "locked");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        using var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.ImportPathAsync(normalBox.Id, sourceDirectory));

        Assert.Equal("ordinary", File.ReadAllText(Path.Combine(sourceDirectory, "a.txt")));
        Assert.Equal("locked", File.ReadAllText(lockedFile));
        Assert.Empty(await workspace.Repository.GetItemsAsync(normalBox.Id));
        Assert.Empty(Directory.GetDirectories(
            normalBox.StoragePath!,
            ".locked-source.witchdrawer-*.tmp"));
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
    public async Task DeleteSettingAsync_RemovesPersistedValueAndReportsWhetherItExisted()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        await workspace.Service.SetSettingAsync("LayoutBackup:1", "saved-layout");

        var deleted = await workspace.Service.DeleteSettingAsync("LayoutBackup:1");
        var deletedAgain = await workspace.Service.DeleteSettingAsync("LayoutBackup:1");

        Assert.True(deleted);
        Assert.False(deletedAgain);
        Assert.Null(await workspace.Service.GetSettingAsync("LayoutBackup:1"));
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
    public async Task DeleteBoxAsync_PixelBoxRestoresItemsToOriginalLocationsAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-p", "boxedpixel.txt", "hello");
        var pixelBox = await workspace.Service.CreateBoxAsync("像素收纳盒 1", BoxType.Pixel);
        var item = await workspace.Service.ImportPathAsync(pixelBox.Id, source);
        var storedPath = item.StoredPath!;

        await workspace.Service.DeleteBoxAsync(pixelBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(pixelBox.Id);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(storedPath));
        Assert.DoesNotContain(boxes, box => box.Id == pixelBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DrawerBox_UsesStoredFileSafetyAndRestoresOnDelete()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("drawer-source", "drawer-item.txt", "hello");
        var drawerBox = await workspace.Service.CreateBoxAsync("抽屉盒 1", BoxType.Drawer);

        var imported = await workspace.Service.ImportPathAsync(drawerBox.Id, source);

        Assert.NotNull(drawerBox.StoragePath);
        Assert.NotNull(imported.StoredPath);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(imported.StoredPath));
        Assert.StartsWith(
            Path.GetFullPath(workspace.Paths.BoxesDirectory),
            Path.GetFullPath(imported.StoredPath!),
            StringComparison.OrdinalIgnoreCase);

        var result = await workspace.Service.DeleteBoxAsync(drawerBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(imported.StoredPath));
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
    public async Task ImportPathAsync_TodoBoxRejectsFileWithoutMovingIt()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var todoBox = await workspace.Service.CreateBoxAsync("todo", BoxType.Todo);
        var sourcePath = workspace.CreateSourceFile("todo-source", "keep.txt", "content");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ImportPathAsync(todoBox.Id, sourcePath));

        Assert.True(File.Exists(sourcePath));
        Assert.Empty(await workspace.Service.GetItemsAsync(todoBox.Id));
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
    public async Task ExportItemToDirectoryAsync_NormalBoxMovesStoredFileAndRemovesItem()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "export-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var oldStoredPath = item.StoredPath!;
        var exportDirectory = Path.Combine(workspace.Root, "desktop");

        var exportedPath = await workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory);
        var remainingItem = await workspace.Repository.GetItemAsync(item.Id);

        Assert.Equal(Path.Combine(exportDirectory, "export-me.txt"), exportedPath);
        Assert.True(File.Exists(exportedPath));
        Assert.False(File.Exists(oldStoredPath));
        Assert.Null(remainingItem);
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_DirectoryContainsLockedFile_PreservesStoredItemAndRecord()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceDirectory = workspace.CreateSourceDirectory("locked-export", "a.txt", "ordinary");
        var lockedSourceFile = Path.Combine(sourceDirectory, "locked.txt");
        File.WriteAllText(lockedSourceFile, "locked");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, sourceDirectory);
        var storedDirectory = item.StoredPath!;
        var storedLockedFile = Path.Combine(storedDirectory, "locked.txt");
        var exportDirectory = Path.Combine(workspace.Root, "desktop");

        using var lockStream = new FileStream(storedLockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory));

        Assert.Equal("ordinary", File.ReadAllText(Path.Combine(storedDirectory, "a.txt")));
        Assert.Equal("locked", File.ReadAllText(storedLockedFile));
        Assert.NotNull(await workspace.Repository.GetItemAsync(item.Id));
        Assert.False(Directory.Exists(Path.Combine(exportDirectory, item.DisplayName)));
        Assert.Empty(Directory.GetDirectories(
            exportDirectory,
            $".{item.DisplayName}.witchdrawer-*.tmp"));
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_AddsSuffixForConflictingName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "export-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "desktop");
        Directory.CreateDirectory(exportDirectory);
        File.WriteAllText(Path.Combine(exportDirectory, "export-me.txt"), "existing");

        var exportedPath = await workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory);

        Assert.Equal(Path.Combine(exportDirectory, "export-me (1).txt"), exportedPath);
        Assert.True(File.Exists(exportedPath));
    }

    [Fact]
    public async Task ExportItemToDirectoryAsync_MappingItemIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "desktop");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ExportItemToDirectoryAsync(item.Id, exportDirectory));

        Assert.True(File.Exists(source));
        Assert.NotNull(await workspace.Repository.GetItemAsync(item.Id));
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
    public async Task DeleteItemAsync_NormalBoxRestoresItemToOriginalLocationAndRemovesItem()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "delete-me.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;

        var result = await workspace.Service.DeleteItemAsync(item.Id);
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(result.WasStoredItem);
        Assert.True(result.RestoredToOriginal);
        Assert.False(result.RestoredToDesktop);
        Assert.Equal(source, result.RestoredPath);
        Assert.True(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(source));
        Assert.False(File.Exists(storedPath));
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteItemAsync_NormalBoxAddsSuffixWhenOriginalPathAlreadyExists()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "conflict.txt", "stored");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        File.WriteAllText(source, "existing");

        var result = await workspace.Service.DeleteItemAsync(item.Id);
        var restoredPath = Path.Combine(Path.GetDirectoryName(source)!, "conflict (1).txt");

        Assert.True(result.RestoredToOriginal);
        Assert.Equal(restoredPath, result.RestoredPath);
        Assert.Equal("existing", File.ReadAllText(source));
        Assert.True(File.Exists(restoredPath));
        Assert.Equal("stored", File.ReadAllText(restoredPath));
        Assert.False(File.Exists(storedPath));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task DeleteItemAsync_FallsBackToDesktopWhenOriginalDirectoryMissing()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-missing", "orphan.txt", "hello");
        var sourceDirectory = Path.GetDirectoryName(source)!;
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        Directory.Delete(sourceDirectory, recursive: true);

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        try
        {
            Assert.True(result.WasStoredItem);
            Assert.False(result.RestoredToOriginal);
            Assert.True(result.RestoredToDesktop);
            Assert.False(string.IsNullOrWhiteSpace(result.RestoredPath));
            Assert.True(File.Exists(result.RestoredPath));
            Assert.Equal("hello", File.ReadAllText(result.RestoredPath!));
            Assert.StartsWith(
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                Path.GetFullPath(result.RestoredPath!),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(storedPath));
            Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(result.RestoredPath) && File.Exists(result.RestoredPath))
            {
                File.Delete(result.RestoredPath);
            }
        }
    }

    [Fact]
    public async Task DeleteItemAsync_MappingBoxOnlyRemovesReference()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var item = await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        Assert.False(result.WasStoredItem);
        Assert.True(File.Exists(source));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Contains("引用", result.StatusMessage);
    }

    [Fact]
    public async Task DeleteBoxAsync_NormalBoxRestoresItemsToOriginalLocationsAndRemovesItems()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "boxed.txt", "hello");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;

        var result = await workspace.Service.DeleteBoxAsync(normalBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(source));
        Assert.False(File.Exists(storedPath));
        Assert.DoesNotContain(boxes, box => box.Id == normalBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task DeleteBoxAsync_KeepsBoxWhenAnyRestoreFails()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var keepSource = workspace.CreateSourceFile("source-keep", "keep.txt", "keep");
        var failSource = workspace.CreateSourceFile("source-fail", "fail.txt", "fail");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var keepItem = await workspace.Service.ImportPathAsync(normalBox.Id, keepSource);
        var failItem = await workspace.Service.ImportPathAsync(normalBox.Id, failSource);

        // Remove the stored file so restore throws FileNotFoundException for this item.
        File.Delete(failItem.StoredPath!);

        var result = await workspace.Service.DeleteBoxAsync(normalBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.False(result.BoxRemoved);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.FailedCount);
        // 失败消息要带首条明细（项目名），否则用户反馈时无法定位是哪一项。
        Assert.Contains("fail.txt", result.StatusMessage);
        Assert.Contains(boxes, box => box.Id == normalBox.Id);
        Assert.True(File.Exists(keepSource));
        Assert.False(File.Exists(keepItem.StoredPath));
        Assert.Single(remainingItems);
        Assert.Equal(failItem.Id, remainingItems[0].Id);
    }

    [Fact]
    public async Task DeleteBoxAsync_MappingBoxOnlyRemovesReferences()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("source-a", "reference.txt", "hello");
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        await workspace.Service.ImportPathAsync(mappingBox.Id, source);

        var result = await workspace.Service.DeleteBoxAsync(mappingBox.Id);
        var boxes = await workspace.Service.GetBoxesAsync();
        var remainingItems = await workspace.Repository.GetItemsAsync(mappingBox.Id);

        Assert.True(result.BoxRemoved);
        Assert.True(File.Exists(source));
        Assert.DoesNotContain(boxes, box => box.Id == mappingBox.Id);
        Assert.Empty(remainingItems);
    }

    [Fact]
    public async Task RenameBoxAsync_RejectsEmptyName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.RenameBoxAsync(normalBox.Id, "   "));
    }

    [Fact]
    public async Task RepositoryMutations_RejectMissingRows()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateBoxNameAsync(missing, "missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveBoxAsync(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateItemGridPositionAsync(missing, 1, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveItemAsync(missing));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.UpdateTodoCompletionAsync(missing, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Repository.RemoveTodoAsync(missing));
    }

    [Fact]
    public async Task ImportPathAsync_DirectoryMovesIntoStorage()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceDir = workspace.CreateSourceDirectory("folder-a", "nested.txt", "payload");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);

        var item = await workspace.Service.ImportPathAsync(normalBox.Id, sourceDir);

        Assert.False(Directory.Exists(sourceDir));
        Assert.NotNull(item.StoredPath);
        Assert.True(Directory.Exists(item.StoredPath));
        Assert.True(File.Exists(Path.Combine(item.StoredPath!, "nested.txt")));
    }

    [Fact]
    public async Task InitializeAsync_CompletesImportedFileAfterMoveBeforeDatabaseInsert()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("interrupted-import", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        File.Move(source, target);

        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();

        Assert.Equal(target, (await workspace.Repository.GetItemAsync(item.Id))?.StoredPath);
        Assert.True(File.Exists(target));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_CompletesBoxMoveWithoutPruningOldRecord()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("interrupted-move", "payload.txt", "payload");
        var oldBox = await workspace.GetBoxAsync(BoxType.Normal);
        var newBox = await workspace.Service.CreateBoxAsync("new", BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(oldBox.Id, source);
        var target = Path.Combine(newBox.StoragePath!, "payload.txt");
        var movedItem = item with { BoxId = newBox.Id, StoredPath = target };
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Move, item.Id,
            item.StoredPath!, target, false, movedItem);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        File.Move(item.StoredPath!, target);

        Assert.Single(await workspace.Service.GetItemsAsync(oldBox.Id));
        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();

        Assert.Equal(newBox.Id, (await workspace.Repository.GetItemAsync(item.Id))?.BoxId);
        Assert.Equal(target, (await workspace.Repository.GetItemAsync(item.Id))?.StoredPath);
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_CompletesExportAfterFileMove()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("interrupted-export", "payload.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "exported");
        Directory.CreateDirectory(exportDirectory);
        var target = Path.Combine(exportDirectory, "payload.txt");
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Remove, item.Id,
            item.StoredPath!, target, false, null);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        File.Move(item.StoredPath!, target);

        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();

        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Equal("payload", File.ReadAllText(target));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_RestoresHeldSourceBeforePromotion()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("interrupted-staging", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(source, operation.Id);
        File.Copy(source, stage);
        File.Move(source, held);

        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();

        Assert.Equal("payload", File.ReadAllText(source));
        Assert.False(File.Exists(stage));
        Assert.False(File.Exists(held));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_AmbiguousTargetPreservesBothFilesAndJournal()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("ambiguous-import", "payload.txt", "source");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        File.WriteAllText(target, "other");

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("other", File.ReadAllText(target));
        Assert.Single(restarted.RecoveryWarnings);
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_UnavailableSourceKeepsJournalAndOtherBoxesUsable()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = Path.Combine(workspace.Root, "offline-drive", "missing.txt");
        var target = Path.Combine(box.StoragePath!, "missing.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "missing.txt", ItemKind.File,
            source, target, 0, now, now);
        await workspace.Repository.AddPendingFileOperationAsync(new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item));

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Single(restarted.RecoveryWarnings);
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
        Assert.NotEmpty(await restarted.GetBoxesAsync());
    }

    [Fact]
    public async Task CompletePendingFileOperation_InvalidItemRollsBackRecordAndJournal()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("rollback-record", "payload.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var target = Path.Combine(box.StoragePath!, "moved.txt");
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Move, item.Id,
            item.StoredPath!, target, false,
            item with { BoxId = Guid.NewGuid(), StoredPath = target });
        await workspace.Repository.AddPendingFileOperationAsync(operation with { ResultItem = operation.ResultItem! with { BoxId = box.Id } });
        File.Move(item.StoredPath!, target);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => workspace.Repository.CompletePendingFileOperationAsync(operation));

        Assert.Equal(box.Id, (await workspace.Repository.GetItemAsync(item.Id))?.BoxId);
        Assert.Equal(item.StoredPath, (await workspace.Repository.GetItemAsync(item.Id))?.StoredPath);
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task InitializeAsync_RestoresHeldDirectoryBeforePromotion()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceDirectory("interrupted-dir", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "interrupted-dir");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "interrupted-dir", ItemKind.Directory,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, true, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(source, operation.Id);
        Directory.CreateDirectory(stage);
        File.Copy(Path.Combine(source, "payload.txt"), Path.Combine(stage, "payload.txt"));
        Directory.Move(source, held);

        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();

        Assert.Equal("payload", File.ReadAllText(Path.Combine(source, "payload.txt")));
        Assert.False(Directory.Exists(stage));
        Assert.False(Directory.Exists(held));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task InitializeAsync_AfterPromotionQuarantinesHeldAndTargetForInspection()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("promoted-import", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        var held = SafeFileOps.CreateHeldSourcePath(source, operation.Id);
        File.Copy(source, target);
        File.Move(source, held);

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Equal("payload", File.ReadAllText(target));
        Assert.Equal("payload", File.ReadAllText(held));
        Assert.Single(restarted.RecoveryWarnings);
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task RecoveryWaitsUntilLiveJournaledOperationReleasesGate()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("inflight-import", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        var stage = SafeFileOps.CreateStagingPath(target, operation.Id);
        await File.WriteAllTextAsync(stage, "partial-copy-in-progress");

        // Simulate a live import holding the gate: recovery triggered by InitializeAsync
        // must not treat this journal entry and its staging file as crash debris.
        var service = workspace.Service;
        await service.FileOperationGate.WaitAsync();
        var initialization = Task.Run(() => service.InitializeAsync());
        try
        {
            var finishedFirst = await Task.WhenAny(
                initialization, Task.Delay(TimeSpan.FromMilliseconds(300))) == initialization;
            Assert.False(finishedFirst, "Recovery ran while a live operation held the gate.");
            Assert.True(File.Exists(stage));
        }
        finally
        {
            service.FileOperationGate.Release();
        }

        await initialization.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(File.Exists(stage));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task CompleteFailure_RestoresMovedFileAndClearsJournal()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("commit-fails", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        // A nonexistent box makes the database commit fail with a foreign-key violation.
        var item = new DrawerItem(
            Guid.NewGuid(), Guid.NewGuid(), "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation with { ResultItem = operation.ResultItem! with { BoxId = box.Id } });
        File.Move(source, target);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.CompleteJournaledOperationAsync(operation));

        Assert.Contains("已放回原位", exception.Message);
        Assert.Equal("payload", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task CompleteFailureWhenRestoreBlocked_KeepsBothFilesAndJournal()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("restore-blocked", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), Guid.NewGuid(), "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation with { ResultItem = operation.ResultItem! with { BoxId = box.Id } });
        File.Move(source, target);
        // A new file occupying the original path makes the best-effort restore fail.
        await File.WriteAllTextAsync(source, "occupied");

        var exception = await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.CompleteJournaledOperationAsync(operation));

        Assert.Contains("下次读取或启动时会重试", exception.Message);
        Assert.Equal("payload", File.ReadAllText(target));
        Assert.Equal("occupied", File.ReadAllText(source));
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());

        // Both paths exist now, so recovery must not guess; it flags manual inspection.
        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();
        Assert.Single(restarted.RecoveryWarnings);
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_DropsJournalWhenMoveNeverStarted()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("never-started", "payload.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "payload.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(), box.Id, "payload.txt", ItemKind.File,
            source, target, 0, now, now);
        // Crash after the journal write but before the first filesystem mutation:
        // recovery must simply drop the journal without touching the source.
        await workspace.Repository.AddPendingFileOperationAsync(new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, false, item));

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Equal("payload", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        Assert.Null(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
        Assert.Empty(restarted.RecoveryWarnings);
    }

    [Fact]
    public async Task CompleteFailure_BoxMove_RestoresFileToOriginalBox()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var oldBox = await workspace.GetBoxAsync(BoxType.Normal);
        var newBox = await workspace.Service.CreateBoxAsync("new", BoxType.Normal);
        var source = workspace.CreateSourceFile("move-commit-fails", "payload.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(oldBox.Id, source);
        var target = Path.Combine(newBox.StoragePath!, "payload.txt");
        // A nonexistent box in the staged result makes the commit fail.
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Move, item.Id,
            item.StoredPath!, target, false,
            item with { BoxId = Guid.NewGuid(), StoredPath = target });
        await workspace.Repository.AddPendingFileOperationAsync(operation with { ResultItem = operation.ResultItem! with { BoxId = newBox.Id } });
        File.Move(item.StoredPath!, target);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.CompleteJournaledOperationAsync(operation));

        Assert.Contains("已放回原位", exception.Message);
        Assert.Equal("payload", File.ReadAllText(item.StoredPath!));
        Assert.False(File.Exists(target));
        Assert.Equal(oldBox.Id, (await workspace.Repository.GetItemAsync(item.Id))?.BoxId);
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task CompleteFailure_Export_RestoresFileIntoBox()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("export-commit-fails", "payload.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var exportDirectory = Path.Combine(workspace.Root, "exported");
        Directory.CreateDirectory(exportDirectory);
        var target = Path.Combine(exportDirectory, "payload.txt");
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Remove, item.Id,
            item.StoredPath!, target, false, null);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        File.Move(item.StoredPath!, target);
        // Force the commit to fail by removing the row it would delete.
        await workspace.Repository.RemoveItemAsync(item.Id);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => workspace.Service.CompleteJournaledOperationAsync(operation));

        Assert.Contains("已放回原位", exception.Message);
        Assert.Equal("payload", File.ReadAllText(item.StoredPath!));
        Assert.False(File.Exists(target));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task AddPendingFileOperation_DuplicateItemIsRejected()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("duplicate-pending", "payload.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var target = Path.Combine(box.StoragePath!, "elsewhere.txt");
        var operation = new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Move, item.Id,
            item.StoredPath!, target, false,
            item with { StoredPath = target });
        await workspace.Repository.AddPendingFileOperationAsync(operation);

        // A second pending operation for the same item would corrupt recovery state.
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => workspace.Repository.AddPendingFileOperationAsync(
                operation with { Id = Guid.NewGuid() }));
        Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Theory]
    [InlineData("witchdrawer.db")]
    [InlineData("witchdrawer.db-wal")]
    [InlineData("storage-location.json")]
    [InlineData("storage-location.json.migration")]
    [InlineData("Boxes")]
    [InlineData("logs")]
    public async Task ImportPathAsync_RejectsApplicationDataWithoutTouchingDatabase(string relativePath)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        await workspace.Service.SetSettingAsync("sentinel", "keep");
        var source = Path.Combine(workspace.Root, relativePath);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            File.WriteAllText(source, "keep");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ImportPathAsync(box.Id, source));

        Assert.Equal("keep", await workspace.Service.GetSettingAsync("sentinel"));
        Assert.Empty(await workspace.Repository.GetItemsAsync(box.Id));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task ImportPathAsync_RejectsManagedFileAndPreservesRestoreLocation()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var other = await workspace.Service.CreateBoxAsync("other", BoxType.Normal);
        var source = workspace.CreateSourceFile("external", "file.txt", "payload");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.ImportPathAsync(other.Id, item.StoredPath!));

        Assert.Single(await workspace.Service.GetItemsAsync(box.Id));
        await workspace.Service.DeleteItemAsync(item.Id);
        Assert.Equal("payload", File.ReadAllText(source));
    }

    [Fact]
    public async Task ImportPathAsync_ConcurrentSameNamesAllocateDistinctSuffixes()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var a = workspace.CreateSourceFile("a", "file.txt", "a");
        var b = workspace.CreateSourceFile("b", "file.txt", "b");
        await workspace.Service.FileOperationGate.WaitAsync();
        Task<DrawerItem> first;
        Task<DrawerItem> second;
        try
        {
            first = workspace.Service.ImportPathAsync(box.Id, a);
            second = workspace.Service.ImportPathAsync(box.Id, b);
        }
        finally
        {
            workspace.Service.FileOperationGate.Release();
        }
        var items = await Task.WhenAll(first, second);
        Assert.Equal(new[] { "file (1).txt", "file.txt" }, items.Select(x => x.DisplayName).OrderBy(x => x));
        Assert.Equal(new[] { "a", "b" }, items.Select(x => File.ReadAllText(x.StoredPath!)).OrderBy(x => x));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task DeleteBoxAsync_RejectsPendingImportEvenWithoutAnItemRow()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("pending", "file.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "file.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), box.Id, "file.txt", ItemKind.File, source, target, 0, now, now);
        await workspace.Repository.AddPendingFileOperationAsync(new PendingFileOperation(
            Guid.NewGuid(), PendingFileOperationKind.Import, item.Id, source, target, false, item));
        File.Move(source, target);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Service.DeleteBoxAsync(box.Id));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => workspace.Repository.RemoveBoxAsync(box.Id));
        Assert.NotNull(await workspace.Repository.GetBoxAsync(box.Id));
        await new DrawerService(workspace.Paths, workspace.Repository).InitializeAsync();
        Assert.NotNull(await workspace.Repository.GetItemAsync(item.Id));
        Assert.Equal("payload", File.ReadAllText(target));
    }

    [Fact]
    public async Task AddPendingFileOperation_RejectsTargetBoxDeletedBeforeJournalWrite()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("late-journal", "file.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "file.txt");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), box.Id, "file.txt", ItemKind.File, source, target, 0, now, now);
        await workspace.Service.DeleteBoxAsync(box.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Repository.AddPendingFileOperationAsync(
            new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
                source, target, false, item)));

        Assert.Equal("payload", File.ReadAllText(source));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task DeleteBoxAsync_SerializesWithQueuedImportsAndPreservesTheirSources()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceFile("queued-import", "file.txt", "keep");
        await workspace.Service.FileOperationGate.WaitAsync();
        Task<BoxDeleteResult> deletion;
        Task<DrawerItem> import;
        try
        {
            deletion = workspace.Service.DeleteBoxAsync(box.Id);
            Assert.False(deletion.IsCompleted);
            import = workspace.Service.ImportPathAsync(box.Id, source);
        }
        finally
        {
            workspace.Service.FileOperationGate.Release();
        }

        Assert.True((await deletion).BoxRemoved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => import);
        Assert.Equal("keep", File.ReadAllText(source));
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Fact]
    public async Task InitializeAsync_CompensationCleanupRemnantsArePreservedForInspection()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = workspace.CreateSourceDirectory("compensated", "file.txt", "payload");
        var target = Path.Combine(box.StoragePath!, "compensated");
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), box.Id, "compensated", ItemKind.Directory, source, target, 0, now, now);
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Import, item.Id,
            source, target, true, item, IsCompensating: true);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        var held = SafeFileOps.CreateHeldSourcePath(target, operation.Id);
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "changed.txt"), "new content");

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Equal("payload", File.ReadAllText(Path.Combine(source, "file.txt")));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(held, "changed.txt")));
        Assert.Contains(held, Assert.Single(restarted.RecoveryWarnings));
        Assert.True(Assert.Single(await workspace.Repository.GetPendingFileOperationsAsync()).IsCompensating);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task InitializeAsync_RecoversInterruptedCompensation(bool isDirectory, bool promoted)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Normal);
        var source = isDirectory
            ? workspace.CreateSourceDirectory("reverse", "file.txt", "payload")
            : workspace.CreateSourceFile("reverse", "file.txt", "payload");
        var target = Path.Combine(box.StoragePath!, Path.GetFileName(source));
        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(Guid.NewGuid(), box.Id, Path.GetFileName(source),
            isDirectory ? ItemKind.Directory : ItemKind.File, source, target, 0, now, now);
        var operation = new PendingFileOperation(Guid.NewGuid(), PendingFileOperationKind.Import,
            item.Id, source, target, isDirectory, item);
        await workspace.Repository.AddPendingFileOperationAsync(operation);
        await workspace.Repository.BeginFileOperationCompensationAsync(operation.Id);
        var held = SafeFileOps.CreateHeldSourcePath(target, operation.Id);
        var stage = SafeFileOps.CreateStagingPath(source, operation.Id);
        if (!promoted)
        {
            if (isDirectory)
            {
                Directory.Move(source, held);
                Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(stage, "partial.txt"), "partial");
            }
            else
            {
                File.Move(source, held);
                File.WriteAllText(stage, "partial");
            }
        }

        var restarted = new DrawerService(workspace.Paths, workspace.Repository);
        await restarted.InitializeAsync();

        Assert.Equal("payload", File.ReadAllText(isDirectory ? Path.Combine(source, "file.txt") : source));
        Assert.False(File.Exists(target) || Directory.Exists(target));
        Assert.False(File.Exists(stage) || Directory.Exists(stage));
        Assert.False(File.Exists(held) || Directory.Exists(held));
        Assert.Empty(restarted.RecoveryWarnings);
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
    }

    [Theory]
    [InlineData("import")]
    [InlineData("mapping")]
    [InlineData("move")]
    [InlineData("export")]
    [InlineData("grid")]
    [InlineData("grids")]
    [InlineData("items")]
    [InlineData("all")]
    [InlineData("boxes")]
    [InlineData("search")]
    public async Task DragOperations_ReturnControlToCallerWhileDatabaseIsLocked(string operation)
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normal = await workspace.GetBoxAsync(BoxType.Normal);
        var mapping = await workspace.GetBoxAsync(BoxType.Mapping);
        var target = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var stored = await workspace.Service.ImportPathAsync(normal.Id,
            workspace.CreateSourceFile("existing", "existing.txt", "original"));
        var source = workspace.CreateSourceFile("new", "new.txt", "new");

        using var blocker = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = workspace.Paths.DatabasePath,
                Pooling = false
            }.ToString());
        blocker.Open();
        using (var command = blocker.CreateCommand())
        {
            command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE; SELECT COUNT(*) FROM Items;";
            command.ExecuteScalar();
        }

        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new Thread(() =>
        {
            try
            {
                Task pending = operation switch
                {
                    "import" => workspace.Service.ImportPathAsync(normal.Id, source),
                    "mapping" => workspace.Service.ImportPathAsync(mapping.Id, source),
                    "move" => workspace.Service.MoveItemToBoxAsync(stored.Id, target.Id),
                    "export" => workspace.Service.ExportItemToDirectoryAsync(stored.Id, Path.Combine(workspace.Root, "export")),
                    "grid" => workspace.Service.UpdateItemGridPositionAsync(stored.Id, 1, 1),
                    "grids" => workspace.Service.UpdateItemGridPositionsAsync(new Dictionary<Guid, (int, int)> { [stored.Id] = (1, 1) }),
                    "items" => workspace.Service.GetItemsAsync(normal.Id),
                    "all" => workspace.Service.GetAllItemsAsync(),
                    "boxes" => workspace.Service.GetBoxesAsync(),
                    "search" => workspace.Service.SearchItemsAsync("existing"),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
                returned.SetResult(pending);
            }
            catch (Exception exception)
            {
                returned.SetException(exception);
            }
        }) { IsBackground = true };

        caller.Start();
        var returnedBeforeUnlock = false;
        try
        {
            // This is a responsiveness check under an intentionally stalled disk query,
            // not a throughput benchmark dependent on the test machine's file speed.
            returnedBeforeUnlock = await Task.WhenAny(returned.Task, Task.Delay(2000)) == returned.Task;
        }
        finally
        {
            blocker.Close();
        }

        var work = await returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await work.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(returnedBeforeUnlock, "The caller was blocked by SQLite before receiving a Task.");
        Assert.Empty(await workspace.Repository.GetPendingFileOperationsAsync());
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

        public string CreateSourceDirectory(string folderName, string nestedFileName, string content)
        {
            var directory = Path.Combine(Root, "sources", folderName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, nestedFileName), content);
            return directory;
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
