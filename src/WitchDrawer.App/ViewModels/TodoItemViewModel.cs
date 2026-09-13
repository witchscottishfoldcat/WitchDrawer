using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class TodoItemViewModel : ObservableObject
{
    private TodoItem _model;
    private bool _isEditing;
    private string _editTitle = string.Empty;

    public TodoItemViewModel(TodoItem model)
    {
        _model = model;
    }

    public TodoItem Model => _model;

    public bool IsEditing { get => _isEditing; private set => SetProperty(ref _isEditing, value); }
    public string EditTitle { get => _editTitle; set => SetProperty(ref _editTitle, value); }
    public string OriginalEditTitle { get; private set; } = string.Empty;

    public void BeginEdit()
    {
        if (IsEditing) return;
        OriginalEditTitle = Title;
        EditTitle = Title;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditTitle = Title;
    }

    public void Update(TodoItem model)
    {
        if (model.Id != Id) throw new ArgumentException("Cannot change a todo's identity.", nameof(model));
        if (_model == model) return;
        _model = model;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(TimeLabel));
    }

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public bool IsCompleted => Model.IsCompleted;

    public string TimeLabel
    {
        get
        {
            var time = (Model.CompletedAt ?? Model.CreatedAt).ToLocalTime();
            var prefix = Model.IsCompleted ? "完成于" : "创建于";
            return $"{prefix} {time:MM-dd HH:mm}";
        }
    }
}
