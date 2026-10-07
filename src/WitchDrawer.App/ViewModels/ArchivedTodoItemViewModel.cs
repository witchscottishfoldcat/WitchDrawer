using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ArchivedTodoItemViewModel : WitchDrawer.App.Localization.LocalizedObservableObject
{
    public ArchivedTodoItemViewModel(TodoItem model, string boxName)
    {
        Model = model;
        BoxName = boxName;
    }

    public TodoItem Model { get; }

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public string BoxName { get; }

    public string ArchivedTimeLabel
    {
        get
        {
            var time = (Model.ArchivedAt ?? Model.UpdatedAt).ToLocalTime();
            return Strings.Format("ArchivedOn", time);
        }
    }
}
