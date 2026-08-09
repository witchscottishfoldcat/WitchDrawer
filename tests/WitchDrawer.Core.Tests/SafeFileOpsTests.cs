using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class SafeFileOpsTests
{
    [Fact]
    public void AreSameVolume_ReturnsTrueForPathsOnSameRoot()
    {
        var root = Path.GetTempPath();
        var a = Path.Combine(root, "witchdrawer-a.txt");
        var b = Path.Combine(root, "nested", "witchdrawer-b.txt");

        Assert.True(SafeFileOps.AreSameVolume(a, b));
    }

    [Fact]
    public void Move_SameVolumeFile_RenamesSourceAway()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("source.txt", "hello");
        var target = Path.Combine(workspace.Root, "target.txt");

        SafeFileOps.Move(source, target, isDirectory: false);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Equal("hello", File.ReadAllText(target));
    }

    [Fact]
    public void Move_SameVolumeDirectory_RenamesSourceAway()
    {
        using var workspace = new TempWorkspace();
        var sourceDir = workspace.CreateDirectory("source-dir");
        File.WriteAllText(Path.Combine(sourceDir, "nested.txt"), "payload");
        var targetDir = Path.Combine(workspace.Root, "target-dir");

        SafeFileOps.Move(sourceDir, targetDir, isDirectory: true);

        Assert.False(Directory.Exists(sourceDir));
        Assert.True(Directory.Exists(targetDir));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(targetDir, "nested.txt")));
    }

    [Fact]
    public void CopyThenDelete_File_CopiesContentAndRemovesSource()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("source.txt", "cross-volume");
        var target = Path.Combine(workspace.Root, "copied.txt");

        SafeFileOps.CopyThenDelete(source, target, isDirectory: false, CancellationToken.None);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Equal("cross-volume", File.ReadAllText(target));
    }

    [Fact]
    public void CopyThenDelete_Directory_CopiesTreeAndRemovesSource()
    {
        using var workspace = new TempWorkspace();
        var sourceDir = workspace.CreateDirectory("source-dir");
        Directory.CreateDirectory(Path.Combine(sourceDir, "child"));
        File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root");
        File.WriteAllText(Path.Combine(sourceDir, "child", "nested.txt"), "nested");
        var targetDir = Path.Combine(workspace.Root, "copied-dir");

        SafeFileOps.CopyThenDelete(sourceDir, targetDir, isDirectory: true, CancellationToken.None);

        Assert.False(Directory.Exists(sourceDir));
        Assert.True(File.Exists(Path.Combine(targetDir, "root.txt")));
        Assert.True(File.Exists(Path.Combine(targetDir, "child", "nested.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(targetDir, "child", "nested.txt")));
    }

    [Fact]
    public void CopyThenDelete_File_LandsAtFinalNameWithoutStagingArtifacts()
    {
        // 文件直接复制到最终名，桌面图标能立刻出现：不应残留 .tmp 暂存文件。
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("source.lnk", "shortcut-payload");
        var target = Path.Combine(workspace.Root, "exported.lnk");

        SafeFileOps.CopyThenDelete(source, target, isDirectory: false, CancellationToken.None);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Equal("shortcut-payload", File.ReadAllText(target));

        var stagingArtifacts = Directory.GetFiles(workspace.Root, "*.witchdrawer-*.tmp");
        Assert.Empty(stagingArtifacts);
    }

    [Fact]
    public void Move_DirectoryIntoOwnDescendant_IsRejectedWithoutCreatingDestination()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        File.WriteAllText(Path.Combine(source, "payload.txt"), "payload");
        var destination = Path.Combine(source, "child");

        Assert.Throws<InvalidOperationException>(() =>
            SafeFileOps.Move(source, destination, isDirectory: true));

        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(destination));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(source, "payload.txt")));
    }

    [Fact]
    public void Move_ThrowsWhenDestinationExists()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("source.txt", "hello");
        var target = workspace.WriteFile("target.txt", "existing");

        Assert.Throws<IOException>(() => SafeFileOps.Move(source, target, isDirectory: false));
        Assert.True(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public void Move_SameVolumeReadOnlyFile_RenamesSourceAway()
    {
        // 同卷 rename 不校验只读位：这条路径本来就不受只读影响。
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("readonly-source.txt", "hello");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        var target = Path.Combine(workspace.Root, "target.txt");
        try
        {
            SafeFileOps.Move(source, target, isDirectory: false);

            Assert.False(File.Exists(source));
            Assert.True(File.Exists(target));
        }
        finally
        {
            if (File.Exists(source))
            {
                File.SetAttributes(source, FileAttributes.Normal);
            }

            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void CopyThenDelete_ReadOnlyFile_RemovesSourceAndKeepsCopyReadOnly()
    {
        // 跨卷回退路径（copy+delete）：File.Copy 保留只读位，而 File.Delete
        // 遇到只读源会抛 UnauthorizedAccessException——删除普通盒子时
        // "Access to the path is denied" 的复现。
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("readonly-source.txt", "payload");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        var target = Path.Combine(workspace.Root, "readonly-target.txt");
        try
        {
            SafeFileOps.CopyThenDelete(source, target, isDirectory: false, CancellationToken.None);

            Assert.False(File.Exists(source));
            Assert.True(File.Exists(target));
            Assert.Equal("payload", File.ReadAllText(target));
            Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(source))
            {
                File.SetAttributes(source, FileAttributes.Normal);
            }

            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void CopyThenDelete_DirectoryWithReadOnlyFile_RemovesSourceTree()
    {
        // Directory.Delete(recursive) 遇到树内只读文件同样抛 UnauthorizedAccessException。
        using var workspace = new TempWorkspace();
        var sourceDir = workspace.CreateDirectory("readonly-dir");
        var innerFile = Path.Combine(sourceDir, "inner.txt");
        File.WriteAllText(innerFile, "inner");
        File.SetAttributes(innerFile, FileAttributes.ReadOnly);
        var targetDir = Path.Combine(workspace.Root, "copied-dir");
        var copiedInner = Path.Combine(targetDir, "inner.txt");
        try
        {
            SafeFileOps.CopyThenDelete(sourceDir, targetDir, isDirectory: true, CancellationToken.None);

            Assert.False(Directory.Exists(sourceDir));
            Assert.True(File.Exists(copiedInner));
            Assert.Equal("inner", File.ReadAllText(copiedInner));
        }
        finally
        {
            if (File.Exists(innerFile))
            {
                File.SetAttributes(innerFile, FileAttributes.Normal);
            }

            if (File.Exists(copiedInner))
            {
                File.SetAttributes(copiedInner, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void CopyThenDelete_SourceDeleteFails_RollbackRemovesReadOnlyCopy()
    {
        // 源被占用（无法删除）时回滚必须连只读副本一起清掉，否则目标位置残留重复文件。
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("locked-readonly.txt", "payload");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        var target = Path.Combine(workspace.Root, "target.txt");
        try
        {
            using var lockStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);

            Assert.Throws<IOException>(
                () => SafeFileOps.CopyThenDelete(source, target, isDirectory: false, CancellationToken.None));
            Assert.True(File.Exists(source));
            Assert.True((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
            Assert.False(File.Exists(target));
        }
        finally
        {
            if (File.Exists(source))
            {
                File.SetAttributes(source, FileAttributes.Normal);
            }

            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "WitchDrawer.SafeFileOps.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string WriteFile(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
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
            }
        }
    }
}

public sealed class SafeFileOpsRollbackTests
{
    [Fact]
    public void CopyThenDelete_DirectoryCopyFailure_RemovesPartialDestinationTree()
    {
        using var workspace = new TempWorkspace();
        var sourceDir = workspace.CreateDirectory("source-dir");
        File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root");
        Directory.CreateDirectory(Path.Combine(sourceDir, "child"));
        File.WriteAllText(Path.Combine(sourceDir, "child", "nested.txt"), "nested");

        // 预置最终目标：操作必须在复制前拒绝它，并保护其中已有内容。
        var targetDir = Path.Combine(workspace.Root, "copied-dir");
        Directory.CreateDirectory(Path.Combine(targetDir, "child"));
        var sentinel = Path.Combine(targetDir, "child", "nested.txt");
        File.WriteAllText(sentinel, "conflict");

        Assert.Throws<IOException>(() =>
            SafeFileOps.CopyThenDelete(sourceDir, targetDir, isDirectory: true, CancellationToken.None));

        Assert.True(Directory.Exists(targetDir));
        Assert.Equal("conflict", File.ReadAllText(sentinel));
        Assert.True(File.Exists(Path.Combine(sourceDir, "root.txt")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "child", "nested.txt")));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "WitchDrawer.SafeFileOps.RollbackTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
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
            catch (IOException)
            {
            }
        }
    }
}
