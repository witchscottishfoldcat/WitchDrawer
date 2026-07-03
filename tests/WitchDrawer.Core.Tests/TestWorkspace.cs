using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

// Shared test scaffolding used by every DrawerService test class. Keeping it as a single
// top-level internal type lets the atomicity and prune suites reuse the same workspace
// setup without duplicating temp-directory plumbing.
internal sealed class TestWorkspace : IDisposable
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

    // For suites that need to inject a custom repository or pre-built service.
    public static TestWorkspace FromExisting(string root, AppPaths paths, DrawerRepository repository, DrawerService service)
    {
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

internal sealed class RecordingTrash : IFileTrash
{
    public List<string> Paths { get; } = [];

    public Task MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default)
    {
        Paths.Add(path);
        return Task.CompletedTask;
    }
}
