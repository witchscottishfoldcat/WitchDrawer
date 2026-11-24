namespace WitchDrawer.Core.Models;

public enum TodoSortMode
{
    Manual,
    NewestFirst,
    OldestFirst
}

public sealed record TodoListSnapshot(TodoSortMode SortMode, IReadOnlyList<TodoItem> Items);
