using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class TodoBoxDetailViewModel : ObservableObject
{
    private readonly TodoService _todoService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private Guid? _boxId;
    private string _newTodoTitle = string.Empty;
    private string _statusText = "从一件小事开始";
    private bool _isBusy;
    private int _busyOperationCount;

    public TodoBoxDetailViewModel(TodoService todoService, IAppLogger logger)
    {
        _todoService = todoService;
        _logger = logger;
        AddTodoCommand = new AsyncRelayCommand(AddTodoAsync, CanAddTodo);
        ToggleTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(ToggleTodoAsync, CanMutateTodo);
        DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(DeleteTodoAsync, CanMutateTodo);
        SaveTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(SaveTodoAsync, CanMutateTodo);
        ArchiveCompletedCommand = new AsyncRelayCommand(ArchiveCompletedAsync, () => BoxId is not null && !IsBusy && HasCompletedTodos);
        UndoDeleteCommand = new AsyncRelayCommand(UndoDeleteAsync, () => !IsBusy && Undo.IsAvailable && Undo.Pending?.BoxId == BoxId);
        Undo.AvailabilityChanged += (_, _) => UndoDeleteCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;
    public ObservableCollection<TodoItemViewModel> ActiveTodos { get; } = [];
    public ObservableCollection<TodoItemViewModel> CompletedTodos { get; } = [];
    public TodoUndoViewModel Undo { get; } = new();
    public IAsyncRelayCommand AddTodoCommand { get; }
    public IAsyncRelayCommand<TodoItemViewModel?> ToggleTodoCommand { get; }
    public IAsyncRelayCommand<TodoItemViewModel?> DeleteTodoCommand { get; }
    public IAsyncRelayCommand<TodoItemViewModel?> SaveTodoCommand { get; }
    public IAsyncRelayCommand ArchiveCompletedCommand { get; }
    public IAsyncRelayCommand UndoDeleteCommand { get; }
    public Guid? BoxId { get => _boxId; private set => SetProperty(ref _boxId, value); }
    public string NewTodoTitle { get => _newTodoTitle; set { if (SetProperty(ref _newTodoTitle, value)) AddTodoCommand.NotifyCanExecuteChanged(); } }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); } }
    public int RemainingCount => ActiveTodos.Count;
    public int CompletedCount => CompletedTodos.Count;
    public int TotalCount => RemainingCount + CompletedCount;
    public int CompletionPercentage => TotalCount == 0 ? 0 : (int)Math.Round(CompletedCount * 100d / TotalCount, MidpointRounding.AwayFromZero);
    public bool IsEmpty => TotalCount == 0;
    public bool HasActiveTodos => RemainingCount > 0;
    public bool HasCompletedTodos => CompletedCount > 0;

    public async Task LoadAsync(Guid? boxId)
    {
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token; var version = ++_loadVersion;
        if (BoxId != boxId) { BoxId = boxId; NewTodoTitle = string.Empty; Undo.Clear(); ApplyItems([]); }
        if (boxId is null) { StatusText = "选择一个待办收纳盒"; return; }
        BeginBusy();
        var entered = false;
        try
        {
            await _gate.WaitAsync(token);
            entered = true;
            var todos = await _todoService.GetTodosAsync(boxId.Value, token);
            if (version != _loadVersion || token.IsCancellationRequested) return;
            ApplyItems(todos); StatusText = IsEmpty ? "从一件小事开始" : $"{RemainingCount} 项待完成";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { _logger.Error(exception, "Failed to load todo box."); if (version == _loadVersion) StatusText = exception.Message; }
        finally { if (entered) _gate.Release(); EndBusy(); }
    }

    private bool CanAddTodo() => BoxId is not null && !IsBusy && NewTodoTitle.Trim().Length is > 0 and <= TodoService.MaximumTitleLength;
    private bool CanMutateTodo(TodoItemViewModel? item) => !IsBusy && item is not null && item.Model.BoxId == BoxId && (ActiveTodos.Contains(item) || CompletedTodos.Contains(item));

    private Task AddTodoAsync() => RunMutationAsync(async (boxId, current) =>
    {
        var title = NewTodoTitle; var item = await _todoService.AddTodoAsync(boxId, title);
        if (current()) { Upsert(item); if (NewTodoTitle == title) NewTodoTitle = string.Empty; }
        return "已添加待办";
    });
    private Task ToggleTodoAsync(TodoItemViewModel? item)
    {
        if (!CanMutateTodo(item)) return Task.CompletedTask;
        var completed = !item!.IsCompleted;
        return RunMutationAsync(async (_, current) => { var updated = await _todoService.SetCompletedAsync(item.Id, completed); if (current()) Upsert(updated); return completed ? "完成一项，做得不错" : "已恢复为待完成"; });
    }
    private Task SaveTodoAsync(TodoItemViewModel? item)
    {
        if (!CanMutateTodo(item) || !item!.IsEditing) return Task.CompletedTask;
        var title = item.EditTitle; var expected = item.OriginalEditTitle;
        return RunMutationAsync(async (_, current) => { var updated = await _todoService.UpdateTitleAsync(item.Id, title, expected); if (current()) { Upsert(updated); item.CancelEdit(); } return "已保存待办"; });
    }
    private Task DeleteTodoAsync(TodoItemViewModel? item)
    {
        if (!CanMutateTodo(item)) return Task.CompletedTask;
        return RunMutationAsync(async (_, current) => { var undo = await _todoService.DeleteWithUndoAsync(item!.Id); if (current()) { ApplyItems(AllModels().Where(x => x.Id != item.Id)); Undo.Offer(undo); } return "已删除待办，10 秒内可撤销"; });
    }
    private Task UndoDeleteAsync()
    {
        var pending = Undo.Pending; if (pending is null || pending.BoxId != BoxId) return Task.CompletedTask;
        return RunMutationAsync(async (_, current) => { var restored = await _todoService.UndoDeleteAsync(pending.Token); if (current()) { Upsert(restored); Undo.Clear(); } return "已撤销删除"; });
    }
    private Task ArchiveCompletedAsync() => RunMutationAsync(async (boxId, current) => { var count = await _todoService.ArchiveCompletedAsync(boxId); var todos = await _todoService.GetTodosAsync(boxId); if (current()) ApplyItems(todos); return count == 0 ? "没有可归档的事项" : $"已归档 {count} 项"; });

    private async Task RunMutationAsync(Func<Guid, Func<bool>, Task<string>> mutate)
    {
        if (IsBusy || BoxId is not Guid boxId) return;
        var version = _loadVersion; bool Current() => version == _loadVersion && BoxId == boxId;
        BeginBusy();
        var entered = false;
        try { await _gate.WaitAsync(); entered = true; var message = await mutate(boxId, Current); if (Current()) StatusText = message; ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId)); }
        catch (Exception exception) { _logger.Error(exception, "Failed to update todo box."); if (Current()) StatusText = exception.Message; }
        finally { if (entered) _gate.Release(); EndBusy(); }
    }

    private void BeginBusy()
    {
        _busyOperationCount++;
        IsBusy = true;
    }

    private void EndBusy()
    {
        if (_busyOperationCount > 0) _busyOperationCount--;
        IsBusy = _busyOperationCount > 0;
    }
    private IEnumerable<TodoItem> AllModels() => ActiveTodos.Concat(CompletedTodos).Select(item => item.Model);
    private void Upsert(TodoItem model) => ApplyItems(TodoCollectionSync.Ordered(AllModels().Where(item => item.Id != model.Id).Append(model)));
    private void ApplyItems(IEnumerable<TodoItem> items)
    {
        var models = items.ToArray(); var existing = ActiveTodos.Concat(CompletedTodos).ToDictionary(item => item.Id);
        TodoCollectionSync.Apply(ActiveTodos, models.Where(item => !item.IsCompleted && !item.IsArchived), existing);
        TodoCollectionSync.Apply(CompletedTodos, models.Where(item => item.IsCompleted && !item.IsArchived), existing);
        foreach (var name in new[] { nameof(RemainingCount), nameof(CompletedCount), nameof(TotalCount), nameof(CompletionPercentage), nameof(IsEmpty), nameof(HasActiveTodos), nameof(HasCompletedTodos) }) OnPropertyChanged(name);
        NotifyCommands();
    }
    private void NotifyCommands() { OnPropertyChanged(nameof(IsBusy)); AddTodoCommand.NotifyCanExecuteChanged(); ToggleTodoCommand.NotifyCanExecuteChanged(); DeleteTodoCommand.NotifyCanExecuteChanged(); SaveTodoCommand.NotifyCanExecuteChanged(); ArchiveCompletedCommand.NotifyCanExecuteChanged(); UndoDeleteCommand.NotifyCanExecuteChanged(); }
}
