using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Controls;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class TodoEditorInteractionTests
{
    [Fact]
    public Task Editor_HandlesSaveCancelAndImeWithoutAddingATask() => RunOnStaAsync(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var row = new TodoItemViewModel(new TodoItem(Guid.NewGuid(), Guid.NewGuid(),
            "原始事项", false, 0, now, now));
        var saved = 0;
        var editor = new TodoTitleEditor
        {
            DataContext = row,
            SaveCommand = new AsyncRelayCommand<TodoItemViewModel?>(item =>
            {
                saved++;
                item!.Update(item.Model with { Title = item.EditTitle });
                item.CancelEdit();
                return Task.CompletedTask;
            })
        };
        using var source = new HwndSource(new HwndSourceParameters("Todo editor test")
        {
            Width = 320, Height = 100, WindowStyle = 0
        });
        source.RootVisual = editor;
        editor.Measure(new Size(320, 100));
        editor.Arrange(new Rect(0, 0, 320, 100));
        var input = (TextBox)editor.FindName("Editor");
        KeyEventArgs Press(Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            editor.RaiseEvent(args);
            return args;
        }

        Assert.True(Press(Key.F2).Handled);
        Assert.True(row.IsEditing);
        input.Text = "修改后的事项";
        Assert.False(Press(Key.ImeProcessed).Handled);
        Assert.Equal(0, saved);
        Assert.True(Press(Key.Enter).Handled);
        Assert.Equal(1, saved);
        Assert.Equal("修改后的事项", row.Title);
        Assert.False(row.IsEditing);
        Press(Key.F2);
        input.Text = "应当丢弃的草稿";
        Assert.True(Press(Key.Escape).Handled);
        Assert.False(row.IsEditing);
        Assert.Equal("修改后的事项", row.Title);
        Assert.Equal(1, saved);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DetailView_RealizesOnlyVisibleRowsAndBindsTheSharedEditor() => RunOnStaAsync(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var model = new TodoBoxDetailViewModel(
            new TodoService(new DrawerRepository("unused-layout-test.db")), NullAppLogger.Instance);
        var boxId = Guid.NewGuid();
        for (var index = 0; index < 1000; index++)
        {
            model.ActiveTodos.Add(new TodoItemViewModel(new TodoItem(Guid.NewGuid(), boxId,
                $"事项 {index + 1}：整理本周资料，检查需要跟进的工作", false, index, now, now)));
        }
        var view = LoadDetailView();
        view.DataContext = model;
        view.Measure(new Size(760, 560));
        view.Arrange(new Rect(0, 0, 760, 560));
        view.UpdateLayout();
        var rows = Descendants<ListBoxItem>(view).ToArray();
        Assert.InRange(rows.Length, 1, 30);
        var editor = Assert.Single(Descendants<TodoTitleEditor>(rows[0]));
        Assert.Same(model.SaveTodoCommand, editor.SaveCommand);
        Assert.True(editor.ActualWidth > 100);

        // Optional artifact for visual inspection without launching the user's app.
        var output = Environment.GetEnvironmentVariable("WITCHDRAWER_TODO_PREVIEW");
        if (!string.IsNullOrEmpty(output))
        {
            var bitmap = new RenderTargetBitmap(760, 560, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(output);
            encoder.Save(stream);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DesktopView_KeepsEditorWithinTheCompactBox(bool empty) => RunOnStaAsync(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var box = new Box(Guid.NewGuid(), "工作清单", BoxType.Todo, null, 0, now, now);
        var repository = new DrawerRepository("unused-desktop-layout-test.db");
        var model = new DesktopBoxViewModel(box,
            new DrawerService(new AppPaths(Path.GetTempPath()), repository),
            new TodoService(repository), new NoOpLauncher(), NullAppLogger.Instance, BoxVisualStyle.Modern);
        if (!empty)
        {
            for (var index = 0; index < 100; index++)
                model.TodoItems.Add(new TodoItemViewModel(new TodoItem(Guid.NewGuid(), box.Id,
                    $"事项 {index + 1}：整理本周资料和需要跟进的工作", false, index, now, now)));
            model.TodoItems[0].BeginEdit();
            model.Undo.Offer(new TodoDeleteUndo(Guid.NewGuid(), box.Id, now.AddSeconds(10)));
        }
        var window = (Window)LoadView("DesktopBoxWindow.xaml");
        try
        {
            window.DataContext = model;
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            content.Arrange(new Rect(content.DesiredSize));
            content.UpdateLayout();
            var list = Assert.Single(Descendants<ListBox>(content), candidate =>
                ReferenceEquals(candidate.ItemsSource, model.TodoItems));
            Assert.Equal(empty ? Visibility.Collapsed : Visibility.Visible, list.Visibility);
            if (empty) return Task.CompletedTask;
            var rows = Descendants<ListBoxItem>(list).ToArray();
            Assert.InRange(rows.Length, 1, 20);
            var editor = Assert.Single(Descendants<TodoTitleEditor>(rows[0]));
            Assert.Same(model.SaveTodoCommand, editor.SaveCommand);
            Assert.InRange(editor.ActualWidth, 140, 265);
            Assert.True(((TextBox)editor.FindName("Editor")).ActualWidth > 80);
            var output = Environment.GetEnvironmentVariable("WITCHDRAWER_TODO_PREVIEW");
            if (!string.IsNullOrEmpty(output))
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
                    (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.ChangeExtension(output, ".desktop.png"));
                encoder.Save(stream);
            }
        }
        finally
        {
            model.Undo.Clear();
            window.Close();
        }
        return Task.CompletedTask;
    });

    private static UserControl LoadDetailView() => (UserControl)LoadView("TodoBoxDetailView.xaml");

    private static FrameworkElement LoadView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln")))
            directory = directory.Parent;
        var source = Path.Combine(directory!.FullName, "src", "WitchDrawer.App");
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = XDocument.Load(Path.Combine(source, "Views", fileName)).Root!;
        root.Attribute(x + "Class")!.Remove();
        root.Attribute("Icon")?.Remove();
        root.DescendantsAndSelf().Attributes().Where(attribute =>
            attribute.Name.LocalName.StartsWith("DeferredIconLoad.") ||
            (attribute.Name.Namespace == XNamespace.None && attribute.Value.StartsWith("On"))).Remove();
        var app = XDocument.Load(Path.Combine(source, "App.xaml")).Root!;
        root.SetAttributeValue(XNamespace.Xmlns + "infra",
            "clr-namespace:WitchDrawer.App.Infrastructure;assembly=WitchDrawer.App");
        var resources = root.Element(p + (root.Name.LocalName + ".Resources"))!;
        (resources.Element(p + "ResourceDictionary") ?? resources).AddFirst(
            app.Element(p + "Application.Resources")!.Elements());
        foreach (var attribute in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            if (attribute.Value.StartsWith("clr-namespace:") && !attribute.Value.Contains(";assembly="))
                attribute.Value += ";assembly=WitchDrawer.App";
        }
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:") &&
                !element.Name.NamespaceName.Contains(";assembly="))
                element.Name = XName.Get(element.Name.LocalName,
                    element.Name.NamespaceName + ";assembly=WitchDrawer.App");
        }
        return (FrameworkElement)XamlReader.Parse(root.ToString());
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Task RunOnStaAsync(Func<Task> test)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); finished.SetResult(); }
                catch (Exception exception) { finished.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
