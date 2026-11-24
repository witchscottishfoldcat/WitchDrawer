using System.Windows.Controls;

using System.Windows.Input;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

public partial class TodoBoxDetailView : UserControl
{
    public TodoBoxDetailView()
    {
        InitializeComponent();
    }

    private void OnNewTodoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not TodoBoxDetailViewModel model ||
            !model.AddTodoCommand.CanExecute(null)) return;

        e.Handled = true;
        model.AddTodoCommand.Execute(null);
    }
}
