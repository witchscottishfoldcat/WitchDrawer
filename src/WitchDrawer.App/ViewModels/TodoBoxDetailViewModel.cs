using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class TodoBoxDetailViewModel : ObservableObject
{
    private readonly TodoService _todoService;
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private Guid? _boxId;
    private string _newTodoTitle = string.Empty;
    private string _statusText = "从一件小事开始";
    private bool _isBusy;

    public TodoBoxDetailViewModel(
        TodoService todoService,
        IAppLogger logger)
    {
        _todoService = todoService;
        _logger = logger;

        AddTodoCommand = new AsyncRelayCommand(AddTodoAsync, CanAddTodo);
        ToggleTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(ToggleTodoAsync);
        DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(DeleteTodoAsync);
        ArchiveCompletedCommand = new AsyncRelayCommand(
            ArchiveCompletedAsync,
            CanArchiveCompleted);
    }

    public event EventHandler? ItemsChanged;

    public ObservableCollection<TodoItemViewModel> ActiveTodos { get; } = [];

    public ObservableCollection<TodoItemViewModel> CompletedTodos { get; } = [];

    public IAsyncRelayCommand AddTodoCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> ToggleTodoCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> DeleteTodoCommand { get; }

    public IAsyncRelayCommand ArchiveCompletedCommand { get; }

    public Guid? BoxId
    {
        get => _boxId;
        private set
        {
            if (SetProperty(ref _boxId, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string NewTodoTitle
    {
        get => _newTodoTitle;
        set
        {
            if (SetProperty(ref _newTodoTitle, value))
            {
                AddTodoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public int RemainingCount => ActiveTodos.Count;

    public int CompletedCount => CompletedTodos.Count;

    public int TotalCount => RemainingCount + CompletedCount;

    public int CompletionPercentage =>
        TotalCount == 0
            ? 0
            : (int)Math.Round(
                CompletedCount * 100d / TotalCount,
                MidpointRounding.AwayFromZero);

    public bool IsEmpty => TotalCount == 0;

    public bool HasActiveTodos => RemainingCount > 0;

    public bool HasCompletedTodos => CompletedCount > 0;

    public async Task LoadAsync(Guid? boxId)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        var version = Interlocked.Increment(ref _loadVersion);

        if (BoxId != boxId)
        {
            BoxId = boxId;
            NewTodoTitle = string.Empty;
        }

        if (boxId is null)
        {
            ReplaceItems([]);
            StatusText = "选择一个待办收纳盒";
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var todos = await _todoService.GetTodosAsync(
                boxId.Value,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentLoad(boxId.Value, version))
            {
                return;
            }

            ReplaceItems(todos.Select(todo => new TodoItemViewModel(todo)));
            StatusText = IsEmpty
                ? "从一件小事开始"
                : $"{RemainingCount} 项待完成";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentLoad(boxId.Value, version))
            {
                return;
            }

            _logger.Error(
                exception,
                $"Failed to load todo box {boxId.Value:N} in the main workspace.");
            StatusText = exception.Message;
        }
        finally
        {
            if (IsCurrentLoad(boxId.Value, version))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanAddTodo()
    {
        var normalizedTitle = NewTodoTitle.Trim();
        return BoxId is not null
            && !IsBusy
            && normalizedTitle.Length is > 0 and <= TodoService.MaximumTitleLength;
    }

    private async Task AddTodoAsync()
    {
        var boxId = BoxId;
        var title = NewTodoTitle;
        if (boxId is null)
        {
            return;
        }

        await RunMutationAsync(
            boxId.Value,
            "add",
            async () =>
            {
                var added = await _todoService.AddTodoAsync(boxId.Value, title);
                _logger.Info(
                    $"Todo detail add completed. BoxId={boxId.Value:N}; TodoId={added.Id:N}.");
                if (BoxId == boxId)
                {
                    NewTodoTitle = string.Empty;
                }

                return "已添加待办";
            });
    }

    private async Task ToggleTodoAsync(TodoItemViewModel? todo)
    {
        var boxId = BoxId;
        if (boxId is null || todo is null || todo.Model.BoxId != boxId.Value)
        {
            return;
        }

        var targetState = !todo.IsCompleted;
        await RunMutationAsync(
            boxId.Value,
            targetState ? "complete" : "reopen",
            async () =>
            {
                await _todoService.SetCompletedAsync(todo.Id, targetState);
                _logger.Info(
                    $"Todo detail completion changed. BoxId={boxId.Value:N}; TodoId={todo.Id:N}; IsCompleted={targetState}.");
                return targetState ? "完成一项，做得不错" : "已恢复为待完成";
            });
    }

    private async Task DeleteTodoAsync(TodoItemViewModel? todo)
    {
        var boxId = BoxId;
        if (boxId is null || todo is null || todo.Model.BoxId != boxId.Value)
        {
            return;
        }

        await RunMutationAsync(
            boxId.Value,
            "delete",
            async () =>
            {
                await _todoService.DeleteTodoAsync(todo.Id);
                _logger.Info(
                    $"Todo detail delete completed. BoxId={boxId.Value:N}; TodoId={todo.Id:N}.");
                return "已删除待办";
            });
    }

    private bool CanArchiveCompleted()
    {
        return BoxId is not null && !IsBusy && HasCompletedTodos;
    }

    private async Task ArchiveCompletedAsync()
    {
        var boxId = BoxId;
        if (boxId is null)
        {
            return;
        }

        await RunMutationAsync(
            boxId.Value,
            "archive",
            async () =>
            {
                var archivedCount = await _todoService.ArchiveCompletedAsync(
                    boxId.Value);
                _logger.Info(
                    $"Todo detail archive completed. BoxId={boxId.Value:N}; Count={archivedCount}.");
                return archivedCount == 0
                    ? "没有可归档的事项"
                    : $"已归档 {archivedCount} 项";
            });
    }

    private async Task RunMutationAsync(
        Guid boxId,
        string operation,
        Func<Task<string>> mutate)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        _logger.Info(
            $"Todo detail mutation started. Operation={operation}; BoxId={boxId:N}.");
        try
        {
            var statusText = await mutate();
            await ReloadAfterMutationAsync(boxId);
            if (BoxId == boxId)
            {
                StatusText = statusText;
            }

            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                $"Todo detail mutation failed. Operation={operation}; BoxId={boxId:N}.");
            if (BoxId == boxId)
            {
                StatusText = exception.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAfterMutationAsync(Guid boxId)
    {
        var todos = await _todoService.GetTodosAsync(boxId);
        if (BoxId != boxId)
        {
            return;
        }

        ReplaceItems(todos.Select(todo => new TodoItemViewModel(todo)));
    }

    private bool IsCurrentLoad(Guid boxId, int version)
    {
        return version == Volatile.Read(ref _loadVersion)
            && BoxId == boxId;
    }

    private void ReplaceItems(IEnumerable<TodoItemViewModel> todos)
    {
        ActiveTodos.Clear();
        CompletedTodos.Clear();
        foreach (var todo in todos)
        {
            if (todo.IsCompleted)
            {
                CompletedTodos.Add(todo);
            }
            else
            {
                ActiveTodos.Add(todo);
            }
        }

        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasActiveTodos));
        OnPropertyChanged(nameof(HasCompletedTodos));
        NotifyCommandStateChanged();
    }

    private void NotifyCommandStateChanged()
    {
        AddTodoCommand.NotifyCanExecuteChanged();
        ArchiveCompletedCommand.NotifyCanExecuteChanged();
    }
}
