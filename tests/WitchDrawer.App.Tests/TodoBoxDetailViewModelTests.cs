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

public sealed class TodoBoxDetailViewModelTests
{
    [Fact]
    public async Task LoadAsync_SeparatesActiveAndCompletedTodos()
    {
        using var workspace = await TodoWorkspace.CreateAsync();
        var active = await workspace.TodoService.AddTodoAsync(
            workspace.TodoBox.Id,
            "整理桌面");
        var completed = await workspace.TodoService.AddTodoAsync(
            workspace.TodoBox.Id,
            "清空下载目录");
        await workspace.TodoService.SetCompletedAsync(completed.Id, isCompleted: true);
        var viewModel = new TodoBoxDetailViewModel(
            workspace.TodoService,
            new RecordingLogger());

        await viewModel.LoadAsync(workspace.TodoBox.Id);

        Assert.Equal(workspace.TodoBox.Id, viewModel.BoxId);
        Assert.Equal(active.Id, Assert.Single(viewModel.ActiveTodos).Id);
        Assert.Equal(completed.Id, Assert.Single(viewModel.CompletedTodos).Id);
        Assert.Equal(1, viewModel.RemainingCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal(2, viewModel.TotalCount);
        Assert.Equal(50, viewModel.CompletionPercentage);
    }

    [Fact]
    public async Task Commands_AddCompleteAndArchiveWithinSelectedTodoBox()
    {
        using var workspace = await TodoWorkspace.CreateAsync();
        var otherBox = await workspace.DrawerService.CreateBoxAsync(
            "其他待办",
            BoxType.Todo);
        await workspace.TodoService.AddTodoAsync(otherBox.Id, "不应被改变");
        var viewModel = new TodoBoxDetailViewModel(
            workspace.TodoService,
            new RecordingLogger());
        var changeCount = 0;
        viewModel.ItemsChanged += (_, _) => changeCount++;
        await viewModel.LoadAsync(workspace.TodoBox.Id);

        viewModel.NewTodoTitle = "  写周报  ";
        await viewModel.AddTodoCommand.ExecuteAsync(null);
        var added = Assert.Single(viewModel.ActiveTodos);
        Assert.Equal("写周报", added.Title);

        await viewModel.ToggleTodoCommand.ExecuteAsync(added);
        var completed = Assert.Single(viewModel.CompletedTodos);
        Assert.Equal(added.Id, completed.Id);

        await viewModel.ArchiveCompletedCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ActiveTodos);
        Assert.Empty(viewModel.CompletedTodos);
        Assert.Equal(3, changeCount);
        Assert.Equal(
            added.Id,
            Assert.Single(
                await workspace.TodoService.GetArchivedTodosAsync(
                    workspace.TodoBox.Id)).Id);
        Assert.Single(await workspace.TodoService.GetTodosAsync(otherBox.Id));
    }

    [Fact]
    public async Task LoadAsync_WithNoBox_ClearsStateAndDisablesMutations()
    {
        using var workspace = await TodoWorkspace.CreateAsync();
        await workspace.TodoService.AddTodoAsync(
            workspace.TodoBox.Id,
            "临时事项");
        var viewModel = new TodoBoxDetailViewModel(
            workspace.TodoService,
            new RecordingLogger());
        await viewModel.LoadAsync(workspace.TodoBox.Id);

        await viewModel.LoadAsync(null);

        Assert.Null(viewModel.BoxId);
        Assert.Empty(viewModel.ActiveTodos);
        Assert.Empty(viewModel.CompletedTodos);
        Assert.False(viewModel.AddTodoCommand.CanExecute(null));
        Assert.False(viewModel.ArchiveCompletedCommand.CanExecute(null));
    }

    [Fact]
    public async Task MainViewModel_SelectingTodoBoxRoutesToTodoDetail()
    {
        using var workspace = await TodoWorkspace.CreateAsync();
        await workspace.TodoService.AddTodoAsync(
            workspace.TodoBox.Id,
            "主面板任务");
        var logger = new RecordingLogger();
        var launcher = new NoOpFileLauncher();
        var visualStyleStore = new BoxVisualStyleStore(
            workspace.DrawerService,
            logger);
        var quickPanel = new QuickPanelViewModel(
            workspace.DrawerService,
            launcher,
            logger,
            visualStyleStore);
        var viewModel = new MainViewModel(
            workspace.DrawerService,
            workspace.TodoService,
            launcher,
            logger,
            quickPanel,
            new UpdateService(logger),
            visualStyleStore,
            new BoxPositionLockStateStore(workspace.DrawerService, logger),
            workspace.Paths,
            new DataStorageMigrationService(
                workspace.Paths,
                workspace.Repository,
                new StorageLocationStore(
                    Path.Combine(workspace.Root, "storage-location.json"))));
        await viewModel.LoadAsync();

        viewModel.SelectedBox = Assert.Single(
            viewModel.Boxes,
            box => box.Id == workspace.TodoBox.Id);
        await viewModel.ReloadItemsFromDesktopAsync();

        Assert.True(viewModel.IsSelectedTodoBox);
        Assert.False(viewModel.CanImportFiles);
        Assert.Empty(viewModel.Items);
        Assert.Equal(
            "主面板任务",
            Assert.Single(viewModel.TodoBoxDetail.ActiveTodos).Title);

        viewModel.SelectedBox = Assert.Single(
            viewModel.Boxes,
            box => box.Type == BoxType.Normal);
        await viewModel.ReloadItemsFromDesktopAsync();

        Assert.False(viewModel.IsSelectedTodoBox);
        Assert.True(viewModel.CanImportFiles);
        Assert.Null(viewModel.TodoBoxDetail.BoxId);
    }

    private sealed class TodoWorkspace : IDisposable
    {
        private TodoWorkspace(
            string root,
            AppPaths paths,
            DrawerRepository repository,
            DrawerService drawerService,
            TodoService todoService,
            Box todoBox)
        {
            Root = root;
            Paths = paths;
            Repository = repository;
            DrawerService = drawerService;
            TodoService = todoService;
            TodoBox = todoBox;
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public DrawerRepository Repository { get; }

        public DrawerService DrawerService { get; }

        public TodoService TodoService { get; }

        public Box TodoBox { get; }

        public static async Task<TodoWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "WitchDrawer.TodoDetailTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var todoBox = await drawerService.CreateBoxAsync(
                "待办收纳盒",
                BoxType.Todo);

            return new TodoWorkspace(
                root,
                paths,
                repository,
                drawerService,
                new TodoService(repository),
                todoBox);
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

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
