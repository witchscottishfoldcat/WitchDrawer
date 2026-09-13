using System.Collections.ObjectModel;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Infrastructure;

internal static class TodoCollectionSync
{
    public static void Apply(ObservableCollection<TodoItemViewModel> target, IEnumerable<TodoItem> models, IDictionary<Guid, TodoItemViewModel>? existing = null)
    {
        existing ??= target.ToDictionary(item => item.Id);
        var desired = models.ToArray();
        var ids = desired.Select(item => item.Id).ToHashSet();
        for (var index = target.Count - 1; index >= 0; index--) if (!ids.Contains(target[index].Id)) target.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            var model = desired[index];
            if (!existing.TryGetValue(model.Id, out var item)) item = new TodoItemViewModel(model); else item.Update(model);
            if (index < target.Count && ReferenceEquals(target[index], item)) continue;
            var previousIndex = target.IndexOf(item);
            if (previousIndex >= 0) target.Move(previousIndex, index); else target.Insert(index, item);
        }
    }
    public static IEnumerable<TodoItem> Ordered(IEnumerable<TodoItem> items) => items.OrderBy(item => item.IsCompleted).ThenBy(item => item.SortOrder).ThenBy(item => item.CreatedAt).ThenBy(item => item.Id);
}
