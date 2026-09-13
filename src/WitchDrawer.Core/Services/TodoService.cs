using WitchDrawer.Core.Models;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

public sealed class TodoService
{
    public const int MaximumTitleLength = 200;
    public static readonly TimeSpan DeleteUndoWindow = TimeSpan.FromSeconds(10);
    private readonly DrawerRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<Guid, (TodoItem Item, DateTimeOffset ExpiresAt)> _undoEntries = [];

    public TodoService(DrawerRepository repository, TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var token in _undoEntries.Where(entry => entry.Value.ExpiresAt <= _timeProvider.GetUtcNow()).Select(entry => entry.Key).ToArray())
                    _undoEntries.Remove(token);
                return await operation().ConfigureAwait(false);
            }
            finally { _operationGate.Release(); }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<TodoItem>> GetTodosAsync(Guid boxId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _repository.GetTodosAsync(boxId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(Guid? boxId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _repository.GetArchivedTodosAsync(boxId, cancellationToken), cancellationToken);

    public Task<TodoItem> AddTodoAsync(Guid boxId, string title, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            await RequireTodoBoxAsync(boxId, cancellationToken);
            var normalizedTitle = NormalizeTitle(title);
            var now = _timeProvider.GetUtcNow();
            var todo = new TodoItem(Guid.NewGuid(), boxId, normalizedTitle, false,
                await _repository.GetNextTodoSortOrderAsync(boxId, cancellationToken), now, now);
            await _repository.AddTodoAsync(todo, cancellationToken);
            return todo;
        }, cancellationToken);

    public Task<TodoItem> UpdateTitleAsync(Guid todoId, string title, string expectedTitle, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var normalizedTitle = NormalizeTitle(title);
            var existing = await RequireTodoAsync(todoId, cancellationToken);
            var updatedAt = _timeProvider.GetUtcNow();
            await _repository.UpdateTodoTitleAsync(todoId, normalizedTitle, expectedTitle, updatedAt, cancellationToken);
            return existing with { Title = normalizedTitle, UpdatedAt = updatedAt };
        }, cancellationToken);

    public Task<TodoItem> SetCompletedAsync(Guid todoId, bool isCompleted, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var existing = await RequireTodoAsync(todoId, cancellationToken);
            if (existing.IsCompleted == isCompleted) return existing;
            var updatedAt = _timeProvider.GetUtcNow();
            DateTimeOffset? completedAt = isCompleted ? updatedAt : null;
            await _repository.UpdateTodoCompletionAsync(todoId, isCompleted, completedAt, updatedAt, cancellationToken);
            return existing with { IsCompleted = isCompleted, CompletedAt = completedAt, UpdatedAt = updatedAt };
        }, cancellationToken);

    public Task DeleteTodoAsync(Guid todoId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => { await _repository.RemoveTodoAsync(todoId, cancellationToken); return true; }, cancellationToken);

    public Task<TodoDeleteUndo> DeleteWithUndoAsync(Guid todoId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var existing = await RequireTodoAsync(todoId, cancellationToken);
            await _repository.RemoveTodoAsync(todoId, cancellationToken);
            var undo = new TodoDeleteUndo(Guid.NewGuid(), existing.BoxId, _timeProvider.GetUtcNow() + DeleteUndoWindow);
            _undoEntries.Add(undo.Token, (existing, undo.ExpiresAt));
            return undo;
        }, cancellationToken);

    public Task<TodoItem> UndoDeleteAsync(Guid token, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            if (!_undoEntries.TryGetValue(token, out var entry)) throw new InvalidOperationException("撤销已过期或已完成。");
            await RequireTodoBoxAsync(entry.Item.BoxId, cancellationToken);
            await _repository.AddTodoAsync(entry.Item, cancellationToken);
            _undoEntries.Remove(token);
            return entry.Item;
        }, cancellationToken);

    public Task<int> ArchiveCompletedAsync(Guid boxId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            await RequireTodoBoxAsync(boxId, cancellationToken);
            return await _repository.ArchiveCompletedTodosAsync(boxId, _timeProvider.GetUtcNow(), cancellationToken);
        }, cancellationToken);

    public Task<TodoItem> RestoreArchivedAsync(Guid todoId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var existing = await RequireTodoAsync(todoId, cancellationToken);
            if (!existing.IsArchived) return existing;
            var updatedAt = _timeProvider.GetUtcNow();
            await _repository.UpdateTodoArchiveStateAsync(todoId, false, null, updatedAt, cancellationToken);
            return existing with { IsArchived = false, ArchivedAt = null, UpdatedAt = updatedAt };
        }, cancellationToken);

    private async Task RequireTodoBoxAsync(Guid boxId, CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken) ?? throw new InvalidOperationException("待办盒不存在或已被删除。");
        if (box.Type != BoxType.Todo) throw new InvalidOperationException("只能操作待办盒中的事项。");
    }
    private async Task<TodoItem> RequireTodoAsync(Guid todoId, CancellationToken cancellationToken) =>
        await _repository.GetTodoAsync(todoId, cancellationToken) ?? throw new InvalidOperationException("待办事项不存在或已被删除。");
    private static string NormalizeTitle(string title)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new ArgumentException("待办内容不能为空。", nameof(title));
        if (normalized.Length > MaximumTitleLength) throw new ArgumentException($"待办内容不能超过 {MaximumTitleLength} 个字符。", nameof(title));
        return normalized;
    }
}
