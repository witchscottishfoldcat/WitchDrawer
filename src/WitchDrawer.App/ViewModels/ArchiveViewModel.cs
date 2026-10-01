using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class ArchiveViewModel
{
    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IAppLogger _logger;
    private readonly UiOperationState _operations;
    private string StatusText { set => _operations.StatusText = value; }
    public ObservableCollection<ArchivedTodoItemViewModel> ArchivedTodos { get; } = [];
    public TodoUndoViewModel ArchiveUndo { get; } = new();
    public IAsyncRelayCommand RestoreArchivedTodoCommand { get; }
    public IAsyncRelayCommand DeleteArchivedTodoCommand { get; }
    public IAsyncRelayCommand UndoArchivedDeleteCommand { get; }

    public ArchiveViewModel(DrawerService drawer, TodoService todo, IAppLogger logger, UiOperationState operations)
    {
        _drawerService = drawer;
        _todoService = todo;
        _logger = logger;
        _operations = operations;
        RestoreArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(RestoreArchivedTodoAsync);
        DeleteArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(DeleteArchivedTodoAsync);
        UndoArchivedDeleteCommand = new AsyncRelayCommand(UndoArchivedDeleteAsync, () => !operations.IsBusy && ArchiveUndo.IsAvailable);
        ArchiveUndo.AvailabilityChanged += (_, _) => UndoArchivedDeleteCommand.NotifyCanExecuteChanged();
        operations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiOperationState.IsBusy)) UndoArchivedDeleteCommand.NotifyCanExecuteChanged();
        };
    }

    private async Task RestoreArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await _operations.RunAsync(async () =>
        {
            await _todoService.RestoreArchivedAsync(todo.Id);
            await LoadAsync();
            StatusText = $"已将“{todo.Title}”恢复到 {todo.BoxName}";
        });
    }

    private async Task DeleteArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await _operations.RunAsync(async () =>
        {
            var undo = await _todoService.DeleteWithUndoAsync(todo.Id);
            ArchivedTodos.Remove(todo);
            ArchiveUndo.Offer(undo);
            StatusText = $"已删除归档事项“{todo.Title}”，10 秒内可撤销";
        });
    }

    private Task UndoArchivedDeleteAsync()
    {
        var pending = ArchiveUndo.Pending;
        if (pending is null) return Task.CompletedTask;
        return _operations.RunAsync(async () =>
        {
            await _todoService.UndoDeleteAsync(pending.Token);
            ArchiveUndo.Clear();
            await LoadAsync();
            StatusText = "已撤销删除归档事项";
        });
    }

    public async Task LoadAsync()
    {
        try
        {
            var archivedTodos = await _todoService.GetArchivedTodosAsync();
            var boxes = await _drawerService.GetBoxesAsync();
            var boxNames = boxes.ToDictionary(box => box.Id, box => box.Name);

            ArchivedTodos.Clear();
            foreach (var todo in archivedTodos)
            {
                var boxName = boxNames.GetValueOrDefault(todo.BoxId, "待办收纳盒");
                ArchivedTodos.Add(new ArchivedTodoItemViewModel(todo, boxName));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load archived todos.");
            StatusText = exception.Message;
        }
    }

}
