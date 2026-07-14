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
    public void Move_ThrowsWhenDestinationExists()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.WriteFile("source.txt", "hello");
        var target = workspace.WriteFile("target.txt", "existing");

        Assert.Throws<IOException>(() => SafeFileOps.Move(source, target, isDirectory: false));
        Assert.True(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(target));
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
