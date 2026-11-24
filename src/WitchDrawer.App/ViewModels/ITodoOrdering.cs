using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public interface ITodoOrdering
{
    TodoSortMode SortMode { get; }
    bool IsBusy { get; }
    Task SetSortModeAsync(TodoSortMode mode);
    Task MoveTodoAsync(TodoItemViewModel source, TodoItemViewModel target, bool after);
}
