using System.IO;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WitchDrawer.App.Plugins;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Plugins;
using WitchDrawer.PluginSdk;

namespace WitchDrawer.App.Tests;

public sealed class PluginRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WitchDrawerPluginRuntimeTests", Guid.NewGuid().ToString("N"));
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string ClockOutput => Path.Combine(RepoRoot, "plugins", "samples", "Clock", "bin",
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name, "net10.0-windows");
    private PluginService Service() => new(new AppPaths(_root), new Version(1, 4, 4), "");

    private Task<InstalledPlugin> InstallClockAsync(PluginService service, bool invalid = false) => InstallAsync(service, ClockOutput, "WitchDrawer.Clock", invalid);

    private async Task<InstalledPlugin> InstallAsync(PluginService service, string output, string assembly, bool invalid = false)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(output, "plugin.json"), "plugin.json");
            if (invalid) { using var stream = zip.CreateEntry(assembly + ".dll").Open(); stream.Write([0, 1, 2]); }
            else
            {
                zip.CreateEntryFromFile(Path.Combine(output, assembly + ".dll"), assembly + ".dll");
                zip.CreateEntryFromFile(Path.Combine(output, assembly + ".deps.json"), assembly + ".deps.json");
            }
        }
        return await service.InstallLocalAsync(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public Task DisabledPluginDoesNotLoadEvenWhenDllIsInvalid() => OnStaAsync(async () =>
    {
        using var service = Service();
        await InstallClockAsync(service, invalid: true);
        var runtime = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        await runtime.EnsureInitializedAsync();
        Assert.False(runtime.IsRunning("witchdrawer.clock"));
        Assert.Null(runtime.GetError("witchdrawer.clock"));
        Assert.False(Assert.Single(await service.GetInstalledAsync()).Enabled);
        await runtime.ShutdownAsync();
    });

    [Fact]
    public Task InvalidEnabledPluginIsDisabledWithoutBreakingStartup() => OnStaAsync(async () =>
    {
        using var service = Service();
        await InstallClockAsync(service, invalid: true);
        await service.SetEnabledAsync("witchdrawer.clock", true);
        var runtime = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        await runtime.EnsureInitializedAsync();
        Assert.False(runtime.IsRunning("witchdrawer.clock"));
        Assert.NotNull(runtime.GetError("witchdrawer.clock"));
        Assert.False(Assert.Single(await service.GetInstalledAsync()).Enabled);
        await runtime.ShutdownAsync();
    });

    [Fact]
    public Task ClockUsesHostSdkAndDisposeStopsItsTimer() => OnStaAsync(async () =>
    {
        using var service = Service();
        var installed = await InstallClockAsync(service);
        var path = await service.VerifyInstalledAsync(installed);
        var context = new PluginLoadContext(path, installed);
        try
        {
            var assembly = context.LoadEntry(path);
            var type = assembly.GetType(installed.Manifest.EntryType)!;
            var widget = Assert.IsAssignableFrom<IWidgetPlugin>(Activator.CreateInstance(type));
            Assert.Same(typeof(IWidgetPlugin).Assembly, type.GetInterfaces().Single(t => t == typeof(IWidgetPlugin)).Assembly);
            var view = Assert.IsType<StackPanel>(widget.CreateView(new WidgetContext(new Dictionary<string, string>(), _ => Task.CompletedTask, _ => { })));
            Assert.NotEmpty(Descendants(view).OfType<TextBlock>().Single(text => text.Name == "ClockTime").Text);
            Assert.IsType<StackPanel>(widget.CreateSettingsView());
            var timer = (DispatcherTimer)type.GetField("_timer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(widget)!;
            Assert.True(timer.IsEnabled);
            widget.Dispose();
            Assert.False(timer.IsEnabled);
        }
        finally { context.Unload(); }
    });

    [Fact]
    public Task RuntimeCanEnableDisableAndRestoreClockOffline() => OnStaAsync(async () =>
    {
        using var service = Service();
        await InstallClockAsync(service);
        var runtime = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        try
        {
            await runtime.EnsureInitializedAsync();
            Assert.False(runtime.IsRunning("witchdrawer.clock"));
            await runtime.EnableAsync("witchdrawer.clock");
            Assert.True(runtime.IsRunning("witchdrawer.clock"));
            await runtime.DisableAsync("witchdrawer.clock");
            Assert.False(runtime.IsRunning("witchdrawer.clock"));
            Assert.False(Assert.Single(await service.GetInstalledAsync()).Enabled);
            await runtime.EnableAsync("witchdrawer.clock");
        }
        finally { await runtime.ShutdownAsync(); }
        Assert.True(Assert.Single(await service.GetInstalledAsync()).Enabled);
        var restored = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        try
        {
            await restored.EnsureInitializedAsync();
            Assert.True(restored.IsRunning("witchdrawer.clock"));
        }
        finally { await restored.ShutdownAsync(); }
    });

    [Fact]
    public Task PerformanceSamplesOffUiAndDisposeStopsItsWorker() => OnStaAsync(async () =>
    {
        using var service = Service();
        var output = ClockOutput.Replace(Path.Combine("Clock", "bin"), Path.Combine("Performance", "bin"), StringComparison.Ordinal);
        var installed = await InstallAsync(service, output, "WitchDrawer.Performance");
        var path = await service.VerifyInstalledAsync(installed);
        var context = new PluginLoadContext(path, installed);
        IWidgetPlugin? widget = null;
        try
        {
            var type = context.LoadEntry(path).GetType(installed.Manifest.EntryType)!;
            widget = Assert.IsAssignableFrom<IWidgetPlugin>(Activator.CreateInstance(type));
            var faults = new List<Exception>();
            var view = widget.CreateView(new WidgetContext(new Dictionary<string, string>(), _ => Task.CompletedTask, faults.Add));
            var texts = Descendants(view).OfType<TextBlock>().ToArray();
            var memory = texts.Single(text => text.Name == "MemoryValue");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (memory.Text == "—" && faults.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.Empty(faults);
            Assert.EndsWith("%", memory.Text);
            Assert.Contains(texts, text => text.Text.Contains("GiB", StringComparison.Ordinal));
            var worker = (Task)type.GetField("_worker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(widget)!;
            Assert.False(worker.IsCompleted);
            widget.Dispose();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
            var valueAfterDispose = memory.Text;
            await Task.Delay(100);
            Assert.Equal(valueAfterDispose, memory.Text);
            Assert.Empty(faults);
            Assert.True(worker.IsCompletedSuccessfully);
            widget.Dispose();
        }
        finally { widget?.Dispose(); context.Unload(); }
    });

    [Fact]
    public Task PerformanceRuntimeRestoresThenDisablesWithoutLoadingHostNativeDependency() => OnStaAsync(async () =>
    {
        using var service = Service();
        var output = ClockOutput.Replace(Path.Combine("Clock", "bin"), Path.Combine("Performance", "bin"), StringComparison.Ordinal);
        await InstallAsync(service, output, "WitchDrawer.Performance");
        var runtime = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        try
        {
            await runtime.EnsureInitializedAsync();
            Assert.False(runtime.IsRunning("witchdrawer.performance"));
            await runtime.EnableAsync("witchdrawer.performance");
            Assert.True(runtime.IsRunning("witchdrawer.performance"));
        }
        finally { await runtime.ShutdownAsync(); }
        Assert.True(Assert.Single(await service.GetInstalledAsync()).Enabled);
        var restored = new PluginRuntime(service, NullAppLogger.Instance, Dispatcher.CurrentDispatcher);
        try
        {
            await restored.EnsureInitializedAsync();
            Assert.True(restored.IsRunning("witchdrawer.performance"));
            await restored.DisableAsync("witchdrawer.performance");
            Assert.False(restored.IsRunning("witchdrawer.performance"));
            Assert.False(Assert.Single(await service.GetInstalledAsync()).Enabled);
        }
        finally { await restored.ShutdownAsync(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Task OnStaAsync(Func<Task> test)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            Exception? failure = null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
            if (failure is null) result.TrySetResult(); else result.TrySetException(failure);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerPluginRuntimeTests")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
