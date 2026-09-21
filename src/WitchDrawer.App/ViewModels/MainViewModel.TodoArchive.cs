namespace WitchDrawer.App.ViewModels;

// 归档页与归档待办事项的恢复、删除、撤销与加载。
public sealed partial class MainViewModel
{
    private async Task ShowArchiveAsync()
    {
        SelectedBox = null;
        IsArchivePage = true;
        IsSettingsPage = false;
        IsAboutPage = false;
        await LoadArchivedTodosAsync();
    }

    private async Task RestoreArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _todoService.RestoreArchivedAsync(todo.Id);
            await LoadArchivedTodosAsync();
            if (SelectedBox?.Id == todo.Model.BoxId)
            {
                await TodoBoxDetail.LoadAsync(todo.Model.BoxId);
            }

            StatusText = $"已将“{todo.Title}”恢复到 {todo.BoxName}";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(todo.Model.BoxId));
        });
    }

    private async Task DeleteArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
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
        return RunBusyAsync(async () =>
        {
            await _todoService.UndoDeleteAsync(pending.Token);
            ArchiveUndo.Clear();
            await LoadArchivedTodosAsync();
            StatusText = "已撤销删除归档事项";
        });
    }

    private async Task LoadArchivedTodosAsync()
    {
        try
        {
            var archivedTodos = await _todoService.GetArchivedTodosAsync();
            var boxNames = Boxes.ToDictionary(box => box.Id, box => box.Name);

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

    private void OnTodoBoxDetailItemsChanged(object? sender, EventArgs e)
    {
        StatusText = TodoBoxDetail.StatusText;
        if (TodoBoxDetail.BoxId is Guid boxId)
        {
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
        }
    }
}
