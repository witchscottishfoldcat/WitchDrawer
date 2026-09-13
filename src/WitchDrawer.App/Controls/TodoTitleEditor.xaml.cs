using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Controls;

public partial class TodoTitleEditor : UserControl
{
    public static readonly DependencyProperty SaveCommandProperty = DependencyProperty.Register(nameof(SaveCommand), typeof(IAsyncRelayCommand), typeof(TodoTitleEditor));
    public static readonly DependencyProperty DeleteCommandProperty = DependencyProperty.Register(nameof(DeleteCommand), typeof(ICommand), typeof(TodoTitleEditor));
    public IAsyncRelayCommand? SaveCommand { get => (IAsyncRelayCommand?)GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }
    public ICommand? DeleteCommand { get => (ICommand?)GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }
    public TodoTitleEditor() => InitializeComponent();

    private void BeginEdit()
    {
        if (DataContext is not TodoItemViewModel todo) return;
        Window.GetWindow(this)?.Activate();
        todo.BeginEdit();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (ReferenceEquals(DataContext, todo) && todo.IsEditing) { Editor.Focus(); Keyboard.Focus(Editor); Editor.SelectAll(); } }));
    }
    private void OnBeginEdit(object sender, RoutedEventArgs e) => BeginEdit();
    private void OnTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { Window.GetWindow(this)?.Activate(); Focus(); if (e.ClickCount == 2) { e.Handled = true; BeginEdit(); } }
    private void OnCancel(object sender, RoutedEventArgs e) { if (DataContext is TodoItemViewModel todo) todo.CancelEdit(); Focus(); }
    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync();
    private async Task SaveAsync()
    {
        var todo = DataContext as TodoItemViewModel;
        if (todo is null || SaveCommand?.CanExecute(todo) != true) return;
        IsEnabled = false;
        try { await SaveCommand.ExecuteAsync(todo); } finally { IsEnabled = true; }
        if (ReferenceEquals(DataContext, todo) && !todo.IsEditing) Focus();
    }
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { e.Handled = true; BeginEdit(); }
        else if (DataContext is TodoItemViewModel { IsEditing: true })
        {
            if (e.Key == Key.Escape) { e.Handled = true; OnCancel(sender, e); }
            else if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); }
        }
    }
}
