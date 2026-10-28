using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class FileNameServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongNameConflicts_ReserveSpaceForEachSuffixAndPreserveTheExtension(bool directory)
    {
        using var workspace = new TempWorkspace();
        var name = new string('a', directory ? 255 : 251) + (directory ? "" : ".txt");
        void Create(string path)
        {
            if (directory) Directory.CreateDirectory(path);
            else File.WriteAllText(path, "content");
        }
        Create(Path.Combine(workspace.Root, name));
        for (var index = 1; index <= 12; index++)
        {
            var result = FileNameService.GetUniqueDestinationPath(workspace.Root, name, directory);
            var fileName = Path.GetFileName(result);
            Assert.True(fileName.Length <= 255);
            Assert.EndsWith($" ({index})" + (directory ? "" : ".txt"), fileName);
            Create(result);
        }
    }

    [Fact]
    public void LongNameConflicts_IncludePendingReservationsWithoutCreatingFiles()
    {
        using var workspace = new TempWorkspace();
        var name = new string('a', 251) + ".txt";
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PathSafety.GetComparisonPath(Path.Combine(workspace.Root, name))
        };
        var first = FileNameService.GetUniqueDestinationPath(workspace.Root, name, false, reserved);
        var second = FileNameService.GetUniqueDestinationPath(workspace.Root, name, false, reserved);
        Assert.EndsWith(" (1).txt", first);
        Assert.EndsWith(" (2).txt", second);
        Assert.Equal(255, Path.GetFileName(first).Length);
        Assert.Equal(255, Path.GetFileName(second).Length);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root));
    }

    [Fact]
    public void LongNameConflicts_DoNotSplitSurrogatePairs()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new TempWorkspace();
        var name = new string('a', 246) + "\U0001F600bbb.txt";
        File.WriteAllText(Path.Combine(workspace.Root, name), "content");
        var result = FileNameService.GetUniqueDestinationPath(workspace.Root, name, false);
        Assert.Equal(new string('a', 246) + " (1).txt", Path.GetFileName(result));
        File.WriteAllText(result, "copy");
        Assert.Equal("copy", File.ReadAllText(result));
    }

    [Fact]
    public void GetUniqueDestinationPath_DirectoryAvoidsExistingFileWithSameName()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "report"), "file");

        var result = FileNameService.GetUniqueDestinationPath(workspace.Root, "report", isDirectory: true);

        Assert.Equal(Path.Combine(workspace.Root, "report (1)"), result);
    }

    [Fact]
    public void GetUniqueDestinationPath_FileAvoidsExistingDirectoryWithSameName()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "report"));

        var result = FileNameService.GetUniqueDestinationPath(workspace.Root, "report", isDirectory: false);

        Assert.Equal(Path.Combine(workspace.Root, "report (1)"), result);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "WitchDrawer.FileNameService.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

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
