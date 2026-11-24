using System.Windows.Controls;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Controls;

public sealed class TodoSortButton : Button
{
    protected override void OnClick()
    {
        base.OnClick();
        if (DataContext is not ITodoOrdering { IsBusy: false } host) return;
        var menu = new TodoPopupMenu(this) { MinWidth = 192 };
        foreach (var mode in Enum.GetValues<TodoSortMode>())
        {
            var option = new MenuItem
            {
                Header = Strings.Get(mode switch
                {
                    TodoSortMode.NewestFirst => "TodoNewestFirst",
                    TodoSortMode.OldestFirst => "TodoOldestFirst",
                    _ => "TodoManualOrder"
                }),
                IsChecked = mode == host.SortMode
            };
            option.Click += async (_, _) =>
            {
                if (ReferenceEquals(DataContext, host) && mode != host.SortMode)
                    await host.SetSortModeAsync(mode);
            };
            menu.Items.Add(option);
        }
        menu.IsOpen = true;
    }
}
