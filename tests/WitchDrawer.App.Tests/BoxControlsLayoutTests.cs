using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class BoxControlsLayoutTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public Task DisplayPopup_UsesDeviceCoordinatesRegardlessOfDpiMetadata(double scale) => RunOnStaAsync(async () =>
    {
        var target = new Button { Width = 112, Height = 34, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(450, 10, 0, 0) };
        var content = new Border { Width = 360, Height = 300 };
        var popup = new Popup { PlacementTarget = target, Child = content, StaysOpen = true };
        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);
        using var source = new HwndSource(new HwndSourceParameters("Display popup alignment test")
        { Width = 900, Height = 500, WindowStyle = 0, PositionX = 100, PositionY = 100 });
        source.RootVisual = host;
        // The native source determines screen coordinates. Changing visual DPI
        // metadata must not make placement drift away from the actual edges.
        VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
        host.Measure(new Size(900, 500));
        host.Arrange(new Rect(0, 0, 900, 500));
        host.UpdateLayout();
        try
        {
            BoxSettingsPopupPlacement.Configure(popup, target, new Size(360, 300));
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var edge = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
            var expectedTop = target.PointToScreen(new Point(0, target.ActualHeight + 6));
            var popupTop = content.PointToScreen(new Point());
            var popupRight = content.PointToScreen(new Point(content.ActualWidth, 0));
            Assert.InRange(Math.Abs(popupRight.X - edge.X), 0, 1);
            Assert.InRange(Math.Abs(popupTop.Y - expectedTop.Y), 0, 1);
        }
        finally { popup.IsOpen = false; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DisplayPopup_ExpandsDownwardsWithoutScrollbarsOrPositionChanges(bool initiallyFixed) => RunOnStaAsync(async () =>
    {
        var sizeSettings = new SizeModeFixture { IsFixedMode = initiallyFixed };
        var view = new BoxDisplaySettingsView { DataContext = CreateLayoutModel(sizeSettings) };
        var content = new Border { Width = 360, Padding = new Thickness(16), Child = view };
        var target = new Button { Width = 112, Height = 34 };
        var popup = new Popup { PlacementTarget = target, Child = content, StaysOpen = true };
        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);
        using var source = new HwndSource(new HwndSourceParameters("Display popup position test")
        { Width = 500, Height = 500, WindowStyle = 0, PositionY = 500 });
        source.RootVisual = host;
        host.Measure(new Size(500, 500));
        host.Arrange(new Rect(0, 0, 500, 500));
        try
        {
            host.UpdateLayout();
            var expanded = view.MeasureExpandedSize(328);
            BoxSettingsPopupPlacement.Configure(popup, target, new Size(360, expanded.Height + 32));
            Assert.Equal(initiallyFixed ? Visibility.Visible : Visibility.Collapsed,
                ((StackPanel)view.FindName("FixedSizeControls")).Visibility);
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var before = content.PointToScreen(new Point());
            var height = content.ActualHeight;
            sizeSettings.IsFixedMode = !initiallyFixed;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (initiallyFixed) Assert.True(content.ActualHeight < height);
            else Assert.True(content.ActualHeight > height);
            var after = content.PointToScreen(new Point());
            Assert.Equal(before.X, after.X, precision: 2);
            Assert.Equal(before.Y, after.Y, precision: 2);
            var controls = (StackPanel)view.FindName("FixedSizeControls");
            Assert.Equal(initiallyFixed ? Visibility.Collapsed : Visibility.Visible, controls.Visibility);
            Assert.Null(FindVisual<ScrollViewer>(view));
            var page = (StackPanel)view.FindName("BoxControlsPageHost");
            var footer = Assert.IsType<TextBlock>(page.Children[^1]);
            var footerBounds = footer.TransformToAncestor(view).TransformBounds(new Rect(footer.RenderSize));
            Assert.InRange(footerBounds.Bottom, 0, view.ActualHeight + 0.01);
        }
        finally { popup.IsOpen = false; }
    });

    [Fact]
    public Task DisplayPopup_RecognizesClicksInNestedDropdowns() => RunOnStaAsync(async () =>
    {
        var view = new BoxDisplaySettingsView();
        var content = new Border { Width = 360, Padding = new Thickness(16), Child = view };
        var target = new Button { Width = 112, Height = 34 };
        var popup = new Popup { PlacementTarget = target, Child = content, StaysOpen = true };
        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);
        using var source = new HwndSource(new HwndSourceParameters("Display popup input test")
        { Width = 500, Height = 500, WindowStyle = 0 });
        source.RootVisual = host;
        host.Measure(new Size(500, 500));
        host.Arrange(new Rect(0, 0, 500, 500));
        try
        {
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var choice = (ComboBox)view.FindName("IconSizeChoice");
            choice.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            choice.UpdateLayout();
            var option = Assert.IsType<ComboBoxItem>(choice.ItemContainerGenerator.ContainerFromIndex(2));
            var label = FindVisual<TextBlock>(option);
            Assert.NotNull(label);
            Assert.False(content.IsAncestorOf(option)); // A different PopupRoot.
            Assert.True(MainWindow.IsWithinBoxPopup(popup, option));
            Assert.True(MainWindow.IsWithinBoxPopup(popup, label));
            Assert.True(MainWindow.IsWithinBoxPopup(popup, choice));
            Assert.False(MainWindow.IsWithinBoxPopup(popup, target));
            Assert.False(MainWindow.IsWithinBoxPopup(popup, host));
        }
        finally
        {
            ((ComboBox)view.FindName("IconSizeChoice")).IsDropDownOpen = false;
            popup.IsOpen = false;
        }
    });

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.25, true)]
    [InlineData(1.5, false)]
    [InlineData(2.0, true)]
    public Task DisplaySettings_FitAtDifferentDpiScales(double scale, bool fixedSize) => RunOnStaAsync(async () =>
    {
        var view = new BoxDisplaySettingsView { DataContext = CreateLayoutModel(new SizeModeFixture { IsFixedMode = fixedSize }) };
        var card = new Border { Padding = new Thickness(16), Child = view, LayoutTransform = new ScaleTransform(scale, scale) };
        card.Measure(new Size(360 * scale, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        card.UpdateLayout();
        foreach (var name in new[] { "VisualStyleChoice", "IconSizeChoice", "HoverChoice", "TitleChoice", "FileNameChoice", "SizeModeChoice" })
        {
            var selector = (ComboBox)view.FindName(name);
            var bounds = selector.TransformToAncestor(view).TransformBounds(new Rect(selector.RenderSize));
            Assert.InRange(bounds.Right, 0, view.ActualWidth + 0.01);
            Assert.InRange(bounds.Bottom, 0, view.ActualHeight + 0.01);
            Assert.Equal(32, selector.ActualHeight);
            Assert.NotNull(selector.SelectedItem);
            var expectedLabel = selector.SelectedItem is BoxVisualStyleOption option ? option.Name
                : ((BoxDisplaySettingsView.Choice)selector.SelectedItem).Label;
            Assert.Contains(expectedLabel, VisualText(selector));
        }
        var fixedControls = (StackPanel)view.FindName("FixedSizeControls");
        Assert.Equal(fixedSize ? Visibility.Visible : Visibility.Collapsed, fixedControls.Visibility);
        if (fixedSize)
        {
            var bounds = fixedControls.TransformToAncestor(view).TransformBounds(new Rect(fixedControls.RenderSize));
            Assert.InRange(bounds.Right, 0, view.ActualWidth + 0.01);
            Assert.InRange(bounds.Bottom, 0, view.ActualHeight + 0.01);
        }
    });

    [Fact]
    public Task DisplayChoices_PersistUserEditsWithoutWritingDuringBindingOrBoxSwitch() => RunOnStaAsync(async () =>
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(temporaryRoot);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawer = new DrawerService(paths, repository);
            await drawer.InitializeAsync();
            var logger = new NoOpLogger();
            var launcher = new NoOpLauncher();
            var styles = new BoxVisualStyleStore(drawer.Settings, logger);
            var model = MainViewModelFactory.Create(drawer, new TodoService(repository), launcher,
                new WitchDrawer.Native.Files.ShellChangeNotifierService(), logger,
                new QuickPanelViewModel(drawer, launcher, logger, styles), new UpdateService(logger), styles,
                new BoxPositionLockStateStore(drawer.Settings, logger), paths,
                new DataStorageMigrationService(paths, repository, new StorageLocationStore(Path.Combine(temporaryRoot, "storage-location.json"))),
                new AutoHideSettingsStore(drawer.Settings));
            await model.CreateNormalBoxCommand.ExecuteAsync(null);
            var box = model.SelectedBox!;
            var before = await drawer.Settings.GetAllSettingsAsync();
            var view = new BoxDisplaySettingsView { DataContext = model };
            _ = view.MeasureExpandedSize(328);
            view.Measure(new Size(328, double.PositiveInfinity));
            view.Arrange(new Rect(view.DesiredSize));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal(before.OrderBy(pair => pair.Key), (await drawer.Settings.GetAllSettingsAsync()).OrderBy(pair => pair.Key));

            await Edit("TitleChoice", false, () => box.ToggleTitleVisibilityCommand.ExecutionTask);
            await Edit("FileNameChoice", true, () => box.ToggleFileNameVisibilityCommand.ExecutionTask);
            await Edit("HoverChoice", true, () => box.ToggleHoverRollUpCommand.ExecutionTask);
            await Edit("IconSizeChoice", "4x4", () => box.LayoutSettings.ApplyPresetCommand.ExecutionTask);
            await Edit("SizeModeChoice", true, () => model.BoxSizeSettings.UseFixedModeCommand.ExecutionTask);
            await Edit("VisualStyleChoice", BoxVisualStyle.Pixel, () => model.SetSelectedBoxVisualStyleCommand.ExecutionTask);
            Assert.Equal(bool.FalseString, await drawer.Settings.GetSettingAsync(BoxViewModel.GetTitleVisibilitySettingKey(box.Id)));
            Assert.Equal(bool.TrueString, await drawer.Settings.GetSettingAsync(BoxViewModel.GetFileNameVisibilitySettingKey(box.Id)));
            Assert.True(box.IsHoverRollUpEnabled);
            Assert.Equal("4x4", await drawer.Settings.GetSettingAsync(BoxViewModel.GetLayoutPresetSettingKey(box.Id)));
            Assert.True(model.BoxSizeSettings.IsFixedMode);
            Assert.Equal(BoxVisualStyle.Pixel, await styles.LoadAsync(box.Model));
            var saved = await drawer.Settings.GetAllSettingsAsync();

            await model.CreateMappingBoxCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.False(model.BoxSizeSettings.HasSelection);
            Assert.Equal(Visibility.Collapsed, ((ComboBox)view.FindName("VisualStyleChoice")).Parent is Grid grid ? grid.Visibility : Visibility.Visible);
            var afterSwitch = await drawer.Settings.GetAllSettingsAsync();
            foreach (var (key, value) in saved) Assert.Equal(value, afterSwitch[key]);
            Assert.DoesNotContain(afterSwitch.Keys, key => key.Contains(model.SelectedBox!.Id.ToString(), StringComparison.OrdinalIgnoreCase));

            async Task Edit(string name, object value, Func<Task?> execution)
            {
                ((ComboBox)view.FindName(name)).SelectedValue = value;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                var task = execution();
                Assert.NotNull(task);
                await task;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
            }
        }
        finally
        {
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerTests")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(expectedParent, Path.GetFullPath(temporaryRoot), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    });

    private sealed class NoOpLauncher : IFileLauncher
    { public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class NoOpLogger : IAppLogger
    { public void Info(string message) { } public void Error(Exception exception, string message) { } }

    private sealed class SizeModeFixture : ObservableObject
    {
        private bool _isFixedMode;
        public bool IsFixedMode { get => _isFixedMode; set => SetProperty(ref _isFixedMode, value); }
        public int FixedColumns => 5;
        public int FixedRows => 5;
        public string ExtentHint => "当前内容占用 1 × 1，固定尺寸不能小于该范围";
    }

    private static object CreateLayoutModel(SizeModeFixture sizeSettings) => new
    {
        BoxVisualStyleOptions = BoxVisualStyleCatalog.Options,
        SelectedBox = new
        {
            SupportsFixedSize = true, SupportsHoverRollUp = true, SupportsFileNameVisibility = true,
            IsHoverRollUpEnabled = true, IsTitleVisible = true, IsFileNameVisible = false,
            CanSelectVisualStyle = true, VisualStyle = BoxVisualStyle.Modern,
            LayoutSettings = new { CurrentPreset = "6x6" }
        },
        BoxSizeSettings = sizeSettings
    };

    private static IEnumerable<string> VisualText(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var value in VisualText(VisualTreeHelper.GetChild(root, index))) yield return value;
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) return result;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
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
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
