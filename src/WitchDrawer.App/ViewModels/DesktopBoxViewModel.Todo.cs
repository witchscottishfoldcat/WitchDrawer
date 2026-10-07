using WitchDrawer.Core.Localization;
using System.Globalization;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的待办部分：待办项增删改、撤销归档与待办面板尺寸逻辑。</summary>
public sealed partial class DesktopBoxViewModel
{
    private bool CanAddTodo()
    {
        return IsTodoBox && !IsBusy && NewTodoTitle.Trim().Length is > 0 and <= TodoService.MaximumTitleLength;
    }

    private Task AddTodoAsync() => RunTodoOperationAsync(async () =>
    {
        var title = NewTodoTitle;
        var item = await _todoService.AddTodoAsync(BoxId, title);
        UpsertTodo(item);
        if (NewTodoTitle == title) NewTodoTitle = string.Empty;
        return Strings.Get("Added");
    });

    private Task ToggleTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return Task.CompletedTask;
        }

        var completed = !todo.IsCompleted;
        return RunTodoOperationAsync(async () =>
        {
            UpsertTodo(await _todoService.SetCompletedAsync(todo.Id, completed));
            return completed ? Strings.Get("Completed") : Strings.Get("Restored");
        });
    }

    private bool CanMutateTodo(TodoItemViewModel? todo) =>
        IsTodoBox && !IsBusy && todo is not null && todo.Model.BoxId == BoxId && TodoItems.Contains(todo);

    private Task SaveTodoAsync(TodoItemViewModel? todo)
    {
        if (!CanMutateTodo(todo) || !todo!.IsEditing) return Task.CompletedTask;
        var title = todo.EditTitle; var expected = todo.OriginalEditTitle;
        return RunTodoOperationAsync(async () =>
        {
            var updated = await _todoService.UpdateTitleAsync(todo.Id, title, expected);
            UpsertTodo(updated); todo.CancelEdit(); return Strings.Get("TaskSaved");
        });
    }

    private bool CanArchiveCompletedTodos()
    {
        return IsTodoBox && !IsBusy && TodoCompletedCount > 0;
    }

    private Task ArchiveCompletedTodosAsync() => RunTodoOperationAsync(async () =>
    {
        var archivedCount = await _todoService.ArchiveCompletedAsync(BoxId);
        ApplyTodoItems(await _todoService.GetTodosAsync(BoxId));
        return archivedCount == 0 ? Strings.Get("NoTasksToArchive") : Strings.Format("ArchivedItems", archivedCount);
    });

    private Task DeleteTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return Task.CompletedTask;
        }

        return RunTodoOperationAsync(async () =>
        {
            var undo = await _todoService.DeleteWithUndoAsync(todo.Id);
            TodoItems.Remove(todo); NotifyTodoState(); Undo.Offer(undo);
            return Strings.Get("DeletedUndoWithin10Seconds");
        });
    }

    private Task UndoDeleteAsync()
    {
        var pending = Undo.Pending;
        if (pending is null || pending.BoxId != BoxId) return Task.CompletedTask;
        return RunTodoOperationAsync(async () =>
        {
            UpsertTodo(await _todoService.UndoDeleteAsync(pending.Token)); Undo.Clear(); return Strings.Get("DeletionUndone");
        });
    }

    private async Task RunTodoOperationAsync(Func<Task<string>> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            NotifyTodoCommands();
            await _todoViewGate.WaitAsync();
            StatusText = await operation();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to update todo box.");
            StatusText = exception.Message;
        }
        finally
        {
            if (_todoViewGate.CurrentCount == 0) _todoViewGate.Release();
            IsBusy = false;
            NotifyTodoCommands();
        }
    }

    private async Task LoadTodoItemsAsync()
    {
        var todos = await _todoService.GetTodosAsync(BoxId);
        ApplyTodoItems(todos);
        if (!IsBusy) StatusText = TodoItems.Count == 0 ? Strings.Get("AddTask") : Strings.Get("Synced");
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(TodoRemainingCount));
        OnPropertyChanged(nameof(TodoCompletedCount));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
    }

    private void UpsertTodo(TodoItem model) => ApplyTodoItems(TodoCollectionSync.Ordered(TodoItems.Select(item => item.Model).Where(item => item.Id != model.Id).Append(model)));
    private void ApplyTodoItems(IEnumerable<TodoItem> items)
    {
        TodoCollectionSync.Apply(TodoItems, items.Where(item => !item.IsArchived));
        NotifyTodoState();
    }
    private void NotifyTodoState()
    {
        OnPropertyChanged(nameof(ItemCountLabel)); OnPropertyChanged(nameof(TodoRemainingCount)); OnPropertyChanged(nameof(TodoCompletedCount)); OnPropertyChanged(nameof(HasTodos)); OnPropertyChanged(nameof(ShowFileEmptyState));
        NotifyTodoCommands();
    }

    private void NotifyTodoCommands()
    {
        AddTodoCommand.NotifyCanExecuteChanged();
        ToggleTodoCommand.NotifyCanExecuteChanged();
        DeleteTodoCommand.NotifyCanExecuteChanged();
        SaveTodoCommand.NotifyCanExecuteChanged();
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
        UndoDeleteCommand.NotifyCanExecuteChanged();
    }

    public void ResizeTodoPanel(double width, double height)
    {
        if (!IsTodoBox)
        {
            return;
        }

        var normalized = NormalizeTodoPanelSize(width, height);
        SetProperty(ref _todoPanelWidth, normalized.Width, nameof(TodoPanelWidth));
        SetProperty(ref _todoPanelHeight, normalized.Height, nameof(TodoPanelHeight));
    }

    public async Task LoadTodoPanelSizeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!IsTodoBox)
        {
            return;
        }

        try
        {
            var saved = await ReadSettingAsync(GetTodoPanelSizeSettingKey(BoxId), snapshot);
            if (TryParseTodoPanelSize(saved, out var width, out var height))
            {
                ResizeTodoPanel(width, height);
            }
            else
            {
                ResizeTodoPanel(DefaultTodoPanelWidth, DefaultTodoPanelHeight);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load todo panel size.");
        }
    }

    public async Task<bool> SaveTodoPanelSizeAsync()
    {
        if (!IsTodoBox)
        {
            return true;
        }

        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{TodoPanelWidth:0.##},{TodoPanelHeight:0.##}");
        try
        {
            await _drawerService.SetSettingAsync(GetTodoPanelSizeSettingKey(BoxId), value);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save todo panel size.");
            StatusText = Strings.Get("CouldNotSaveSize");
            return false;
        }
    }

    internal static string GetTodoPanelSizeSettingKey(Guid boxId) =>
        $"{TodoPanelSizeSettingPrefix}{boxId:N}";

    internal static (double Width, double Height) NormalizeTodoPanelSize(double width, double height)
    {
        var normalizedWidth = double.IsFinite(width) ? width : DefaultTodoPanelWidth;
        var normalizedHeight = double.IsFinite(height) ? height : DefaultTodoPanelHeight;
        return (
            Math.Clamp(normalizedWidth, MinimumTodoPanelWidth, MaximumTodoPanelWidth),
            Math.Clamp(normalizedHeight, MinimumTodoPanelHeight, MaximumTodoPanelHeight));
    }

    internal static bool TryParseTodoPanelSize(string? value, out double width, out double height)
    {
        width = 0;
        height = 0;
        var parts = value?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            && double.IsFinite(width)
            && double.IsFinite(height)
            && width > 0
            && height > 0;
    }
}
