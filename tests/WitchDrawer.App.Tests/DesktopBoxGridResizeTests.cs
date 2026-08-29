using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

/// <summary>
/// All boxes are adaptive-only: the fixed m x n grid size was removed, so
/// applying a fixed size mode is always a no-op and the box stays adaptive.
/// </summary>
public sealed class DesktopBoxGridResizeTests
{
    [Fact]
    public async Task NormalBox_DoesNotSupportFixedSize()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("normal", BoxType.Normal);
            var viewModel = CreateViewModel(box, drawerService, repository);

            Assert.False(viewModel.SupportsFixedSize);

            viewModel.ApplySizeMode(new BoxSizeModeState(true, 5, 2));

            Assert.False(viewModel.IsFixedSize);
            Assert.Equal(BoxSizeModeState.Adaptive, viewModel.SizeMode);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task MappingBox_DoesNotSupportFixedSize()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("mapping", BoxType.Mapping);
            var viewModel = CreateViewModel(box, drawerService, repository);

            Assert.False(viewModel.SupportsFixedSize);

            viewModel.ApplySizeMode(new BoxSizeModeState(true, 4, 4));

            Assert.False(viewModel.IsFixedSize);
            Assert.Equal(BoxSizeModeState.Adaptive, viewModel.SizeMode);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task TodoBox_DoesNotSupportFixedSize()
    {
        var root = CreateTempRoot();
        try
        {
            var (drawerService, repository) = await CreateDrawerServiceAsync(root);
            var box = await drawerService.CreateBoxAsync("todo", BoxType.Todo);
            var viewModel = CreateViewModel(box, drawerService, repository);

            Assert.False(viewModel.SupportsFixedSize);

            viewModel.ApplySizeMode(new BoxSizeModeState(true, 3, 3));

            Assert.False(viewModel.IsFixedSize);
            Assert.Equal(BoxSizeModeState.Adaptive, viewModel.SizeMode);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static DesktopBoxViewModel CreateViewModel(
        Box box,
        DrawerService drawerService,
        DrawerRepository repository) =>
        new(
            box,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateDrawerServiceAsync(
        string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var drawerService = new DrawerService(paths, repository);
        await drawerService.InitializeAsync();
        return (drawerService, repository);
    }

    private static void CleanupTempRoot(string root)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}