using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServiceAdvancedTests
{
    [Fact]
    public async Task RenameItemAsync_ChangesDisplayNameOnly_NotTheFileOnDisk()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var file = workspace.CreateSourceFile("rename", "old-name.txt", "content");
        var item = await workspace.Service.ImportPathAsync(box.Id, file);

        await workspace.Service.RenameItemAsync(item.Id, "新显示名");

        var items = await workspace.Service.GetItemsAsync(box.Id);
        var renamed = Assert.Single(items);
        Assert.Equal("新显示名", renamed.DisplayName);
        Assert.True(File.Exists(file), "原文件必须保持原名且不被移动。");
        Assert.Equal("old-name.txt", Path.GetFileName(file));
    }

    [Fact]
    public async Task RenameItemAsync_KeepsLinkSuffixForShortcutNames()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var file = workspace.CreateSourceFile("rename", "app.lnk", "dummy shortcut");
        var item = await workspace.Service.ImportPathAsync(box.Id, file);

        await workspace.Service.RenameItemAsync(item.Id, "我的应用");

        var items = await workspace.Service.GetItemsAsync(box.Id);
        Assert.Equal("我的应用.lnk", Assert.Single(items).DisplayName);
    }

    [Fact]
    public async Task RenameItemAsync_RejectsEmptyName()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var file = workspace.CreateSourceFile("rename", "a.txt", "content");
        var item = await workspace.Service.ImportPathAsync(box.Id, file);

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.RenameItemAsync(item.Id, "   "));
    }

    [Fact]
    public async Task OpenItemAsAdminAsync_LaunchesWithAdminVerbForFiles()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var file = workspace.CreateSourceFile("launch", "tool.exe", "binary");
        var item = await workspace.Service.ImportPathAsync(box.Id, file);
        var launcher = new RecordingFileLauncher();

        await workspace.Service.OpenItemAsAdminAsync(item.Id, launcher);

        Assert.Equal(Path.GetFullPath(file), launcher.AdminOpened.Single());
    }

    [Fact]
    public async Task OpenItemAsAdminAsync_RejectsDirectories()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var directory = workspace.CreateSourceDirectory("launch", "nested.txt", "content");
        var item = await workspace.Service.ImportPathAsync(box.Id, directory);
        var launcher = new RecordingFileLauncher();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.OpenItemAsAdminAsync(item.Id, launcher));
    }

    [Fact]
    public async Task ShowItemInFolderAsync_RevealsTheEffectivePath()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var box = await workspace.GetBoxAsync(BoxType.Mapping);
        var file = workspace.CreateSourceFile("launch", "doc.pdf", "pdf");
        var item = await workspace.Service.ImportPathAsync(box.Id, file);
        var launcher = new RecordingFileLauncher();

        await workspace.Service.ShowItemInFolderAsync(item.Id, launcher);

        Assert.Equal(Path.GetFullPath(file), launcher.Revealed.Single());
    }

    [Fact]
    public async Task MergeBoxAsync_MovesStoredFilesAndRemovesSourceBox()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("来源盒", BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("目标盒", BoxType.Normal);
        var file = workspace.CreateSourceFile("merge", "a.txt", "hello");
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, file);
        var originalStoredPath = item.StoredPath!;
        Assert.True(File.Exists(originalStoredPath));

        var mergedCount = await workspace.Service.MergeBoxAsync(sourceBox.Id, targetBox.Id);

        Assert.Equal(1, mergedCount);
        Assert.DoesNotContain(await workspace.Service.GetBoxesAsync(), box => box.Id == sourceBox.Id);
        Assert.False(File.Exists(originalStoredPath), "文件应被移出源盒存储目录。");

        var items = await workspace.Service.GetItemsAsync(targetBox.Id);
        var moved = Assert.Single(items);
        Assert.Equal(item.Id, moved.Id);
        Assert.NotNull(moved.StoredPath);
        Assert.True(File.Exists(moved.StoredPath), "文件应存在于目标盒存储目录。");
    }

    [Fact]
    public async Task MergeBoxAsync_RepointsMappingReferencesWithoutTouchingFiles()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("来源映射盒", BoxType.Mapping);
        var targetBox = await workspace.Service.CreateBoxAsync("目标映射盒", BoxType.Mapping);
        var file = workspace.CreateSourceFile("merge-map", "photo.png", "image");
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, file);
        Assert.Null(item.StoredPath);

        var mergedCount = await workspace.Service.MergeBoxAsync(sourceBox.Id, targetBox.Id);

        Assert.Equal(1, mergedCount);
        Assert.True(File.Exists(file), "映射盒合并不能移动源文件。");
        var items = await workspace.Service.GetItemsAsync(targetBox.Id);
        Assert.Equal(item.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task MergeBoxAsync_RejectsMixingMappingAndStorageBoxes()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normalBox = await workspace.Service.CreateBoxAsync("普通盒", BoxType.Normal);
        var mappingBox = await workspace.Service.CreateBoxAsync("映射盒", BoxType.Mapping);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(normalBox.Id, mappingBox.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(mappingBox.Id, normalBox.Id));
    }

    [Fact]
    public async Task MergeBoxAsync_RejectsTodoBoxesAndSelfMerge()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normalBox = await workspace.Service.CreateBoxAsync("普通盒", BoxType.Normal);
        var todoBox = await workspace.Service.CreateBoxAsync("待办盒", BoxType.Todo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(normalBox.Id, normalBox.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(normalBox.Id, todoBox.Id));
    }

    [Fact]
    public async Task ClassifyMappingItemsAsync_GroupsReferencesIntoTypeBoxes()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var photo = workspace.CreateSourceFile("classify", "photo.png", "image");
        var doc = workspace.CreateSourceFile("classify", "readme.md", "markdown");
        var video = workspace.CreateSourceFile("classify", "clip.mp4", "video");
        await workspace.Service.ImportPathAsync(mappingBox.Id, photo);
        await workspace.Service.ImportPathAsync(mappingBox.Id, doc);
        await workspace.Service.ImportPathAsync(mappingBox.Id, video);

        var result = await workspace.Service.ClassifyMappingItemsAsync();

        Assert.Equal(3, result.MovedCount);
        Assert.Equal(0, result.SkippedCount);
        var boxes = await workspace.Service.GetBoxesAsync();
        Assert.Contains(boxes, box => box.Name == "图片收纳盒" && box.Type == BoxType.Mapping);
        Assert.Contains(boxes, box => box.Name == "文档收纳盒" && box.Type == BoxType.Mapping);
        Assert.Contains(boxes, box => box.Name == "视频收纳盒" && box.Type == BoxType.Mapping);
    }

    [Fact]
    public async Task ClassifyMappingItemsAsync_SkipsStoredItemsAndReusesExistingBoxes()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var mappingBox = await workspace.GetBoxAsync(BoxType.Mapping);
        var storedFile = workspace.CreateSourceFile("classify", "stored.png", "image");
        await workspace.Service.ImportPathAsync(normalBox.Id, storedFile);
        var reference = workspace.CreateSourceFile("classify", "ref.png", "image");
        await workspace.Service.ImportPathAsync(mappingBox.Id, reference);

        var first = await workspace.Service.ClassifyMappingItemsAsync();
        Assert.Equal(1, first.MovedCount);
        Assert.Equal(1, first.SkippedCount);

        // 第二次运行时引用已在分类盒内，不再产生移动。
        var second = await workspace.Service.ClassifyMappingItemsAsync();
        Assert.Equal(0, second.MovedCount);
        var imageBoxes = (await workspace.Service.GetBoxesAsync())
            .Where(box => box.Name == "图片收纳盒")
            .ToArray();
        Assert.Single(imageBoxes);
    }

    [Fact]
    public void ClassifyCategory_AssignsExtensionsToExpectedCategories()
    {
        Assert.Equal("图片收纳盒", ClassifyCategory.GetCategory("a.JPG", null).BoxName);
        Assert.Equal("文档收纳盒", ClassifyCategory.GetCategory("b.docx", null).BoxName);
        Assert.Equal("视频收纳盒", ClassifyCategory.GetCategory("c.MKV", null).BoxName);
        Assert.Equal("音频收纳盒", ClassifyCategory.GetCategory("d.flac", null).BoxName);
        Assert.Equal("压缩包收纳盒", ClassifyCategory.GetCategory("e.zip", null).BoxName);
        Assert.Equal("文件夹收纳盒", ClassifyCategory.GetCategory("folder", null, isDirectory: true).BoxName);
        Assert.Equal("其他收纳盒", ClassifyCategory.GetCategory("no-extension", null).BoxName);
        Assert.Equal("其他收纳盒", ClassifyCategory.GetCategory("f.xyz", null).BoxName);
        Assert.Equal("其他收纳盒", ClassifyCategory.GetCategory("", null).BoxName);
    }

    [Fact]
    public async Task MergeBoxAsync_RollsBackMovedItemsWhenLaterItemFails()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("来源盒", BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("目标盒", BoxType.Normal);
        var firstFile = workspace.CreateSourceFile("merge-rollback", "first.txt", "a");
        var secondFile = workspace.CreateSourceFile("merge-rollback", "second.txt", "b");
        var firstItem = await workspace.Service.ImportPathAsync(sourceBox.Id, firstFile);
        var secondItem = await workspace.Service.ImportPathAsync(sourceBox.Id, secondFile);

        // 第二个 item 的存储文件被外部删除 → 合并中途失败。
        File.Delete(secondItem.StoredPath!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(sourceBox.Id, targetBox.Id));

        Assert.Contains("回滚", exception.Message);
        // 第一个 item 应已回滚到源盒。
        var sourceItems = await workspace.Service.GetItemsAsync(sourceBox.Id);
        Assert.Contains(sourceItems, item => item.Id == firstItem.Id);
        Assert.True(File.Exists(firstItem.StoredPath!), "回滚后文件应仍在源盒存储目录。");
    }

    [Fact]
    public async Task MergeBoxAsync_PreflightRejectsStoredItemInsideMappingBox()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("来源映射盒", BoxType.Mapping);
        var targetBox = await workspace.Service.CreateBoxAsync("目标映射盒", BoxType.Mapping);

        // 直接注入异常数据：映射盒里出现一条带存储路径的 item。
        var abnormalItem = new DrawerItem(
            Guid.NewGuid(),
            sourceBox.Id,
            "abnormal.txt",
            ItemKind.File,
            "C:\\fake\\original.txt",
            "C:\\fake\\stored.txt",
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        await workspace.Repository.AddItemAsync(abnormalItem);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Service.MergeBoxAsync(sourceBox.Id, targetBox.Id));

        // 源盒与目标盒都不应发生变化（直接查仓库，避开 Service 的缺失文件清理）。
        Assert.Contains(
            await workspace.Repository.GetItemsAsync(sourceBox.Id),
            item => item.Id == abnormalItem.Id);
        Assert.Empty(await workspace.Repository.GetItemsAsync(targetBox.Id));
    }

    [Fact]
    public void CreateImportPermissionException_MentionsMappingBoxForPublicDesktop()
    {
        var commonDesktop = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDesktopDirectory);
        if (string.IsNullOrWhiteSpace(commonDesktop))
        {
            return; // 环境无公共桌面时跳过。
        }

        var sourcePath = Path.Combine(commonDesktop, "微信.lnk");
        var exception = DrawerService.CreateImportPermissionException(
            "微信.lnk",
            sourcePath,
            new UnauthorizedAccessException("Access denied"));

        Assert.Contains("映射收纳盒", exception.Message, StringComparison.Ordinal);
        Assert.Contains("公共桌面", exception.Message, StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    [Fact]
    public void CreateImportPermissionException_GenericHintForOtherReadOnlyLocations()
    {
        var exception = DrawerService.CreateImportPermissionException(
            "a.txt",
            "C:\\Program Files\\SomeApp\\a.txt",
            new UnauthorizedAccessException("Access denied"));

        Assert.Contains("映射收纳盒", exception.Message, StringComparison.Ordinal);
        Assert.Contains("源目录没有写入权限", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingFileLauncher : IFileLauncher
    {
        public List<string> Opened { get; } = [];

        public List<string> AdminOpened { get; } = [];

        public List<string> Revealed { get; } = [];

        public Task OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            Opened.Add(Path.GetFullPath(path));
            return Task.CompletedTask;
        }

        public Task OpenAsAdminAsync(string path, CancellationToken cancellationToken = default)
        {
            AdminOpened.Add(Path.GetFullPath(path));
            return Task.CompletedTask;
        }

        public Task ShowInFolderAsync(string path, CancellationToken cancellationToken = default)
        {
            Revealed.Add(Path.GetFullPath(path));
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
                // Best-effort test cleanup.
            }
        }
    }
}
