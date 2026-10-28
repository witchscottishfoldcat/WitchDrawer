using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Plugins;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Plugins;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class WidgetThemeTests
{
    [Theory]
    [InlineData(AppTheme.Moe, "#FFFFFFFF")]
    [InlineData(AppTheme.Glass, "#D12C2C2E")]
    [InlineData(AppTheme.Crystal, "#66FFFFFF")]
    public Task NewWidgetUsesCurrentDesktopSurface(AppTheme theme, string surface) => WithThemeAsync(async () =>
    {
        AppThemeManager.Apply(theme);
        var window = CreateWindow();
        try
        {
            await DrainAsync();
            var root = Assert.IsType<Border>(window.Content);
            Assert.Equal(Parse(surface), BrushColor(root.Background));
            Assert.Equal(new CornerRadius(20), root.CornerRadius);
            Assert.Equal(1, window.Opacity);
        }
        finally { window.ForceClose(); }
    });

    [Fact]
    public Task RunningWidgetFollowsThemeOpacityColorsBorderAndCorners() => WithThemeAsync(async () =>
    {
        AppThemeManager.Apply(AppTheme.Moe);
        var window = CreateWindow();
        try
        {
            var root = Assert.IsType<Border>(window.Content);
            AppThemeManager.Apply(AppTheme.Glass);
            await DrainAsync();
            Assert.Equal(Parse("#D12C2C2E"), BrushColor(root.Background));

            AppThemeManager.Apply(AppTheme.Crystal);
            await DrainAsync();
            Assert.Equal(Parse("#66FFFFFF"), BrushColor(root.Background));

            AppThemeManager.SetBoxOpacity(AppTheme.Crystal, 0.1);
            AppThemeManager.SetCustomization(AppTheme.Crystal, new ThemeCustomization
            {
                BoxBackground = "#123456", BoxCornerRadius = 7, Accent = "#AF52DE"
            });
            AppThemeManager.SetBoxBorderOpacity(AppTheme.Crystal, 0);
            await DrainAsync();
            Assert.Equal(Parse("#1A123456"), BrushColor(root.Background));
            Assert.Equal(0, BrushColor(root.BorderBrush).A);
            Assert.Equal(new CornerRadius(7), root.CornerRadius);
            Assert.Equal(1, window.Opacity); // Surface transparency must not fade the clock text.

            AppThemeManager.SetCustomization(AppTheme.Crystal, ThemeCustomization.Empty);
            AppThemeManager.SetBoxOpacity(AppTheme.Crystal, 0.4);
            AppThemeManager.SetBoxBorderOpacity(AppTheme.Crystal, null);
            await DrainAsync();
            Assert.Equal(Parse("#66FFFFFF"), BrushColor(root.Background));
            Assert.Equal(new CornerRadius(20), root.CornerRadius);
            Assert.Equal(102, BrushColor(root.BorderBrush).A);
        }
        finally { window.ForceClose(); }
    });

    [Fact]
    public Task HostedTextInheritsLiveThemeForeground() => WithThemeAsync(async () =>
    {
        AppThemeManager.Apply(AppTheme.Moe);
        var text = new TextBlock { Text = "12:34:56" };
        var window = CreateWindow(text);
        try
        {
            // Isolated fallback dictionary stands in for the application's shared resources.
            var shared = new ResourceDictionary { ["TextPrimaryBrush"] = Brushes.Black };
            window.Resources.MergedDictionaries.Add(shared);
            await DrainAsync();
            Assert.Equal(Colors.Black, BrushColor(text.Foreground));
            shared["TextPrimaryBrush"] = Brushes.White;
            await DrainAsync();
            Assert.Equal(Colors.White, BrushColor(text.Foreground));
        }
        finally { window.ForceClose(); }
    });

    [Fact]
    public Task ClosedWidgetCancelsPendingRefreshAndStopsFollowingThemeEvents() => WithThemeAsync(async () =>
    {
        AppThemeManager.Apply(AppTheme.Crystal);
        var window = CreateWindow();
        var original = window.Resources["GlassSurfaceBrush"];
        AppThemeManager.SetBoxOpacity(AppTheme.Crystal, 0.1);
        window.ForceClose();
        await DrainAsync();
        Assert.Same(original, window.Resources["GlassSurfaceBrush"]);

        AppThemeManager.Apply(AppTheme.Glass);
        AppThemeManager.SetBoxOpacity(AppTheme.Glass, 0.2);
        AppThemeManager.SetCustomization(AppTheme.Glass, new ThemeCustomization { BoxBackground = "#123456" });
        await DrainAsync();
        Assert.Same(original, window.Resources["GlassSurfaceBrush"]);
    });

    [Fact]
    public Task SavedSizeRestoresAndContentsScaleFromTop() => WithThemeAsync(() =>
    {
        var view = new Border { Width = 280, Height = 100 };
        var window = new WidgetWindow(new InstalledPlugin
        {
            Manifest = new PluginManifest { Name = "Resizable widget" }, Width = 480, Height = 240
        }, view);
        try
        {
            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.Equal(480, window.Width);
            Assert.Equal(240, window.Height);
            var root = Assert.IsType<Border>(window.Content);
            root.Measure(new Size(480, 240)); root.Arrange(new Rect(0, 0, 480, 240)); root.UpdateLayout();
            var scaled = Assert.IsType<Viewbox>(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(view)));
            var grown = view.TransformToAncestor(scaled).TransformBounds(new Rect(view.RenderSize));
            Assert.True(grown.Width > 280);
            Assert.Equal(VerticalAlignment.Top, scaled.VerticalAlignment);
            root.Measure(new Size(240, 130)); root.Arrange(new Rect(0, 0, 240, 130)); root.UpdateLayout();
            var shrunk = view.TransformToAncestor(scaled).TransformBounds(new Rect(view.RenderSize));
            Assert.True(shrunk.Width < 280);
        }
        finally { window.ForceClose(); }
        return Task.CompletedTask;
    });

    private static WidgetWindow CreateWindow(FrameworkElement? view = null) => new(
        new InstalledPlugin { Manifest = new PluginManifest { Name = "Theme test widget" } },
        view ?? new TextBlock { Text = "12:34:56" });

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static async Task DrainAsync() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    private static Task WithThemeAsync(Func<Task> test)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Exception? failure = null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                var previous = AppThemeManager.CurrentTheme;
                AppThemeManager.ResetBoxOpacitiesForTests();
                try { await test(); }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    AppThemeManager.ResetBoxOpacitiesForTests();
                    AppThemeManager.Apply(previous);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
            if (failure is null) result.TrySetResult(); else result.TrySetException(failure);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
