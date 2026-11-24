using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Localization;

namespace WitchDrawer.App.Controls;

public partial class TodoReorderHandle : Thumb
{
    private ListBox? _list;
    private TodoItemViewModel? _source;
    private ListBoxItem? _target;
    private bool _after;
    private bool _dragged;
    private Point _origin;
    private InsertionLine? _line;

    public TodoReorderHandle() => InitializeComponent();

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null && node is not T) node = VisualTreeHelper.GetParent(node);
        return node as T;
    }

    private void OnStart(object sender, DragStartedEventArgs e)
    {
        _list = Ancestor<ListBox>(this);
        _source = DataContext as TodoItemViewModel;
        _origin = Mouse.GetPosition(_list);
        _dragged = false;
    }

    private void OnMove(object sender, DragDeltaEventArgs e)
    {
        if (_list is null || _source is null) return;
        var point = Mouse.GetPosition(_list);
        if (!_dragged && Math.Abs(point.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(point.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        _dragged = true;
        if (!new Rect(_list.RenderSize).Contains(point))
        {
            Mark(null, false);
            return;
        }
        var scroll = Ancestor<ScrollViewer>(this);
        if (point.Y < 12) scroll?.LineUp();
        else if (point.Y > _list.ActualHeight - 12) scroll?.LineDown();

        var hit = _list.InputHitTest(point) as DependencyObject;
        var row = hit is null ? null : ItemsControl.ContainerFromElement(_list, hit) as ListBoxItem;
        if (row?.DataContext is not TodoItemViewModel target || target.Id == _source.Id ||
            target.IsCompleted != _source.IsCompleted)
        {
            Mark(null, false);
            return;
        }
        Mark(row, Mouse.GetPosition(row).Y >= row.ActualHeight / 2);
    }

    private void Mark(ListBoxItem? row, bool after)
    {
        if (row == _target && after == _after) return;
        if (_line?.Parent is AdornerLayer layer) layer.Remove(_line);
        _target = row;
        _after = after;
        _line = null;
        if (row is null || AdornerLayer.GetAdornerLayer(row) is not { } adorners) return;
        _line = new InsertionLine(row, after);
        adorners.Add(_line);
    }

    private async void OnFinish(object sender, DragCompletedEventArgs e)
    {
        var source = _source;
        var list = _list;
        var target = _target?.DataContext as TodoItemViewModel;
        var after = _after;
        Mark(null, false);
        _source = null;
        _list = null;
        if (e.Canceled || list?.DataContext is not ITodoOrdering host || source is null) return;
        if (!_dragged) OpenMoveMenu(list, source, host);
        else if (target is not null && new Rect(list.RenderSize).Contains(Mouse.GetPosition(list)))
            await host.MoveTodoAsync(source, target, after);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsDragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelDrag();
        }
        else if (e.Key is Key.Space or Key.Enter && Ancestor<ListBox>(this) is { DataContext: ITodoOrdering host } list
                 && DataContext is TodoItemViewModel source)
        {
            e.Handled = true;
            OpenMoveMenu(list, source, host);
        }
        else base.OnKeyDown(e);
    }

    private void OpenMoveMenu(ListBox list, TodoItemViewModel source, ITodoOrdering host)
    {
        var index = list.Items.IndexOf(source);
        if (index < 0 || host.IsBusy) return;
        var menu = new TodoPopupMenu(this);
        foreach (var offset in new[] { -1, 1 })
        {
            var next = index + offset;
            var target = next >= 0 && next < list.Items.Count ? list.Items[next] as TodoItemViewModel : null;
            var item = new MenuItem
            {
                Header = Strings.Get(offset < 0 ? "MoveTodoUp" : "MoveTodoDown"),
                IsEnabled = target is not null && target.IsCompleted == source.IsCompleted
            };
            item.Click += async (_, _) =>
            {
                if (target is not null) await host.MoveTodoAsync(source, target, offset > 0);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private sealed class InsertionLine(ListBoxItem row, bool after) : Adorner(row)
    {
        protected override void OnRender(DrawingContext context)
        {
            var y = after ? row.ActualHeight - 2 : 0;
            context.DrawRectangle((Brush)row.FindResource("AccentBrush"), null, new Rect(0, y, row.ActualWidth, 2));
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => null;
    }
}
