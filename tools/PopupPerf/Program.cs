using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

internal static class Program
{
    // Isolated WPF popup using the actual app template and Opened handler.
    // No database is opened and no desktop-host window is shown.
    [STAThread]
    private static void Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Dispatcher.BeginInvoke(async () =>
        {
            try { await MeasureAsync(args); }
            catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
            finally { application.Shutdown(); }
        });
        application.Run();
    }

    private static async Task MeasureAsync(string[] args)
    {
        var sourceDirectory = Path.Combine(Environment.CurrentDirectory, "src", "WitchDrawer.App");
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = new XElement(p + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", x),
            new XAttribute(XNamespace.Xmlns + "infra", "clr-namespace:WitchDrawer.App.Infrastructure;assembly=WitchDrawer.App"),
            XDocument.Load(Path.Combine(sourceDirectory, "App.xaml")).Root!
                .Element(p + "Application.Resources")!.Elements());
        foreach (var element in resources.Descendants())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:")
                && !element.Name.NamespaceName.Contains(";assembly="))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=WitchDrawer.App");
        }
        Application.Current.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        AppThemeManager.Apply(AppTheme.Moe);

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerPopupPerf", Guid.NewGuid().ToString("N")));
        var repository = new DrawerRepository(paths.DatabasePath);
        var now = DateTimeOffset.UtcNow;
        var box = new Box(Guid.NewGuid(), "Popup performance probe", BoxType.Drawer, null, 0, now, now);
        var viewModel = new DesktopBoxViewModel(box, new DrawerService(paths, repository),
            new TodoService(repository), new NoOpLauncher(), NullAppLogger.Instance, BoxVisualStyle.Modern);
        for (var index = 0; index < 45; index++)
        {
            viewModel.Items.Add(new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), box.Id,
                $"File {index}", ItemKind.File, null, null, index, now, now)));
        }
        viewModel.ApplyFileNameVisibility(true);
        viewModel.SyncDrawerSecondaryFromItems();
        var owner = new DesktopBoxWindow(viewModel);
        var popup = (Popup)owner.FindName("DrawerSecondaryPopup");
        var root = (FrameworkElement)owner.FindName("DrawerSecondaryPopupRoot");
        var scale = (ScaleTransform)owner.FindName("DrawerSecondaryPopupScale");
        ((Panel)LogicalTreeHelper.GetParent(popup)).Children.Remove(popup);
        popup.DataContext = viewModel;
        popup.Placement = PlacementMode.Absolute;
        popup.HorizontalOffset = SystemParameters.WorkArea.Left + 100;
        popup.VerticalOffset = SystemParameters.WorkArea.Top + 100;
        popup.StaysOpen = true;
        var host = new Window
        {
            Width = 360, Height = 100, ShowInTaskbar = false, ShowActivated = false,
            Left = SystemParameters.WorkArea.Left + 100, Top = SystemParameters.WorkArea.Top + 450,
            Content = popup, Title = "WitchDrawer popup measurement"
        };
        host.Show();
        await host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
        var results = new List<object>();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            typeof(DesktopBoxWindow).GetMethod("PrepareDrawerSecondaryPopupForOpen",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(owner, null);
            var watch = Stopwatch.StartNew();
            var renders = new List<double>();
            var cacheChanges = new List<object>();
            var layouts = 0;
            var sizes = 0;
            var windowPositions = 0;
            EventHandler rendered = (_, _) => renders.Add(watch.Elapsed.TotalMilliseconds);
            EventHandler layoutUpdated = (_, _) => layouts++;
            EventHandler cacheChanged = (_, _) => cacheChanges.Add(new
            {
                atMs = watch.Elapsed.TotalMilliseconds, enabled = root.CacheMode is not null
            });
            HwndSourceHook hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x0005) sizes++;
                if (message == 0x0047) windowPositions++;
                return nint.Zero;
            };
            HwndSource? source = null;
            EventHandler opened = (_, _) =>
            {
                source = (HwndSource)PresentationSource.FromVisual(root);
                source.AddHook(hook);
            };
            var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.CacheModeProperty, root.GetType());
            descriptor.AddValueChanged(root, cacheChanged);
            root.LayoutUpdated += layoutUpdated;
            CompositionTarget.Rendering += rendered;
            popup.Opened += opened;
            popup.IsOpen = true;
            var openMs = watch.Elapsed.TotalMilliseconds;
            await Task.Delay(320);
            CompositionTarget.Rendering -= rendered;
            root.LayoutUpdated -= layoutUpdated;
            descriptor.RemoveValueChanged(root, cacheChanged);
            source?.RemoveHook(hook);
            popup.Opened -= opened;
            var intervals = renders.Zip(renders.Skip(1), (first, second) => second - first).ToArray();
            results.Add(new
            {
                iteration, openMs, layouts, sizes, windowPositions,
                firstRenderMs = renders.FirstOrDefault(), renderCallbacks = renders.Count,
                maxRenderGapMs = intervals.DefaultIfEmpty().Max(),
                gapsOver16Ms = intervals.Count(value => value > 16.67), cacheChanges
            });
            popup.IsOpen = false;
            await Task.Delay(180);
        }
        host.Close();
        owner.ForceClose();
        var json = JsonSerializer.Serialize(new
        {
            renderTier = RenderCapability.Tier >> 16,
            note = "WPF UI rendering callbacks, not GPU presentation FPS; synthetic file labels, no user data or database IO.",
            results
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (args.Length > 0) File.WriteAllText(args[0], json);
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
