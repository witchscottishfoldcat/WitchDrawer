using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class QuickPanelLazyLoadTests
{
    [Fact]
    public async Task IncrementalRefresh_BeforeFirstLoad_IsSkipped()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            await ImportFileAsync(service, root, box.Id, "a.txt");
            var viewModel = CreateViewModel(service);

            await viewModel.RefreshBoxAsync(box.Id);
            await viewModel.RefreshAllAsync();

            Assert.Empty(viewModel.Items);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task FirstOpen_LoadsEverythingIncludingChangesBeforeOpen()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var boxA = await service.CreateBoxAsync("盒子A", BoxType.Normal);
            await ImportFileAsync(service, root, boxA.Id, "a.txt");
            var viewModel = CreateViewModel(service);

            // 尚未初始化时收到文件变更：不触发加载，只等首次打开完整加载。
            await viewModel.RefreshBoxAsync(boxA.Id);
            var boxB = await service.CreateBoxAsync("盒子B", BoxType.Mapping);
            var mappingSource = Path.Combine(root, "source", "b.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(mappingSource)!);
            await File.WriteAllTextAsync(mappingSource, "b");
            await service.ImportPathAsync(boxB.Id, mappingSource);

            await viewModel.EnsureLoadedAsync();

            Assert.Equal(2, viewModel.Items.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RepeatedOpen_DoesNotRescan_IncrementalRefreshStillWorks()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            await ImportFileAsync(service, root, box.Id, "a.txt");
            var viewModel = CreateViewModel(service);
            await viewModel.EnsureLoadedAsync();
            Assert.Single(viewModel.Items);

            // 已初始化后重复打开不再全量扫描：新增文件不会凭空出现。
            await ImportFileAsync(service, root, box.Id, "b.txt");
            await viewModel.EnsureLoadedAsync();
            Assert.Single(viewModel.Items);

            // 现有增量刷新路径继续生效。
            await viewModel.RefreshBoxAsync(box.Id);
            Assert.Equal(2, viewModel.Items.Count);

            // 运行期间的显式全量刷新也继续生效。
            await ImportFileAsync(service, root, box.Id, "c.txt");
            await viewModel.RefreshAllAsync();
            Assert.Equal(3, viewModel.Items.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task FirstLoadFailure_KeepsUninitializedAndRetries()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            await ImportFileAsync(service, root, box.Id, "a.txt");
            var viewModel = CreateViewModel(service);

            // 数据库不可读：首次加载失败，保持未初始化状态，允许下次重试。
            DeleteDirectoryWithRetry(root);
            await viewModel.EnsureLoadedAsync();
            Assert.Empty(viewModel.Items);

            await service.InitializeAsync();
            var recoveredBox = (await service.GetBoxesAsync())[0];
            await ImportFileAsync(service, root, recoveredBox.Id, "recovered.txt");
            await viewModel.EnsureLoadedAsync();

            Assert.Single(viewModel.Items);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task ImportFileAsync(
        DrawerService service,
        string root,
        Guid boxId,
        string fileName)
    {
        var sourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, $"{Guid.NewGuid():N}-{fileName}");
        await File.WriteAllTextAsync(sourceFile, "data");
        await service.ImportPathAsync(boxId, sourceFile);
    }

    private static QuickPanelViewModel CreateViewModel(DrawerService service) =>
        new(
            service,
            new NoOpFileLauncher(),
            new RecordingLogger(),
            new BoxVisualStyleStore(service, new RecordingLogger()));

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateServiceAsync(string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var service = new DrawerService(paths, repository);
        await service.InitializeAsync();
        return (service, repository);
    }


    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
