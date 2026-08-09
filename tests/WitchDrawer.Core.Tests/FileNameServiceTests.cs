using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class FileNameServiceTests
{
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
