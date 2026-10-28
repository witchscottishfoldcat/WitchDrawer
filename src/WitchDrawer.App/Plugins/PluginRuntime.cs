using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Plugins;
using WitchDrawer.PluginSdk;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Plugins;

/// <summary>UI lifecycle only; package and state mutations always go through Core.</summary>
internal sealed class PluginRuntime(PluginService service, IAppLogger logger, Dispatcher dispatcher)
{
    private sealed record Session(IWidgetPlugin Plugin, PluginLoadContext Context, WidgetWindow Window);
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, string> _errors = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _initialization;
    private bool _closing;
    private DesktopLayerMonitor? _monitor;
    private DesktopLayerUpdateQueue? _updates;
    private nint _desktopHost;
    public event Action? Changed;
    public bool IsRunning(string id) => _sessions.ContainsKey(id);
    public string? GetError(string id) => _errors.GetValueOrDefault(id);

    public Task EnsureInitializedAsync() => _initialization ??= InitializeAsync();
    private async Task InitializeAsync()
    {
        try
        {
            foreach (var plugin in await service.GetInstalledAsync())
            {
                if (_closing) break;
                if (!plugin.Enabled) continue;
                try { await EnableAsync(plugin.Manifest.Id); }
                catch (Exception exception)
                {
                    _errors[plugin.Manifest.Id] = exception.GetBaseException().Message;
                    logger.Error(exception, "Plugin startup failed: " + plugin.Manifest.Id);
                    try { await service.SetEnabledAsync(plugin.Manifest.Id, false); }
                    catch (Exception writeException) { logger.Error(writeException, "Failed to disable broken plugin."); }
                }
            }
        }
        catch (Exception exception) { logger.Error(exception, "Failed to restore installed plugins."); }
        Changed?.Invoke();
    }

    public async Task EnableAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_closing || _sessions.ContainsKey(id)) return;
            var installed = (await service.GetInstalledAsync()).Single(p => p.Manifest.Id == id);
            var path = await service.VerifyInstalledAsync(installed);
            var (context, type) = await Task.Run(() => LoadType(path, installed));
            if (_closing) { context.Unload(); return; }
            IWidgetPlugin? plugin = null;
            WidgetWindow? window = null;
            try
            {
                plugin = (IWidgetPlugin)Activator.CreateInstance(type)!;
                var settings = new WidgetContext(installed.Settings,
                    values => service.SaveSettingsAsync(id, values),
                    exception => dispatcher.BeginInvoke(() =>
                    {
                        if (_sessions.TryGetValue(id, out var current) && ReferenceEquals(current.Plugin, plugin))
                            FireAndForget.Run(HandleFaultAsync(id, exception), logger, "Plugin fault cleanup failed.");
                    }), () => Strings.Culture);
                var view = plugin.CreateView(settings) ?? throw new InvalidOperationException("Plugin returned no view.");
                window = new WidgetWindow(installed, view);
                window.CloseRequested += () => FireAndForget.Run(DisableAsync(id), logger, "Plugin disable failed.");
                window.LayoutChanged += () => FireAndForget.Run(SaveLayoutAsync(id, window), logger, "Plugin layout save failed.");
                window.DesktopChanged += () => _updates?.Request();
                window.Show();
                _sessions.Add(id, new Session(plugin, context, window));
                await service.SetEnabledAsync(id, true);
                _errors.Remove(id);
                EnsureMonitoring();
                _updates?.Request();
            }
            catch
            {
                _sessions.Remove(id);
                window?.ForceClose();
                try { plugin?.Dispose(); } catch (Exception exception) { logger.Error(exception, "Plugin dispose failed after startup failure."); }
                context.Unload();
                throw;
            }
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private static (PluginLoadContext, Type) LoadType(string path, InstalledPlugin installed)
    {
        var context = new PluginLoadContext(path, installed);
        try
        {
            var assembly = context.LoadEntry(path);
            var type = assembly.GetType(installed.Manifest.EntryType, throwOnError: true)!;
            if (!typeof(IWidgetPlugin).IsAssignableFrom(type) || type.IsAbstract || !type.IsPublic || type.GetConstructor(Type.EmptyTypes) is null)
                throw new InvalidOperationException("Plugin entry must be a public IWidgetPlugin with a default constructor.");
            return (context, type);
        }
        catch { context.Unload(); throw; }
    }

    public async Task DisableAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_closing) return;
            if (_sessions.TryGetValue(id, out var session))
                await SaveLayoutAsync(id, session.Window);
            // Persist intent before removing the window; an I/O error leaves a usable running widget.
            await service.SetEnabledAsync(id, false);
            Stop(id);
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async Task HandleFaultAsync(string id, Exception exception)
    {
        if (_closing) return;
        _errors[id] = exception.GetBaseException().Message;
        logger.Error(exception, "Plugin reported a fault: " + id);
        try { await DisableAsync(id); }
        catch { Stop(id); Changed?.Invoke(); throw; }
    }

    public void ShowSettings(string id, Window owner)
    {
        if (!_sessions.TryGetValue(id, out var session)) return;
        try
        {
            var view = session.Plugin.CreateSettingsView();
            if (view is null)
            {
                MessageBox.Show(owner, Strings.Get("PluginNoSettings"), session.Window.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new Window { Title = session.Window.Title, Content = view, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 280, MaxWidth = 800, MaxHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.SetResourceReference(Window.BackgroundProperty, "PanelBrush");
            dialog.ShowDialog();
            dialog.Content = null;
        }
        catch (Exception exception) { FireAndForget.Run(HandleFaultAsync(id, exception), logger, "Plugin settings fault cleanup failed."); }
    }

    private void Stop(string id)
    {
        if (!_sessions.Remove(id, out var session)) return;
        session.Window.ForceClose();
        try { session.Plugin.Dispose(); }
        catch (Exception exception) { logger.Error(exception, "Plugin dispose failed: " + id); }
        session.Context.Unload(); // Cooperative; locked old packages are cleaned after restart.
        if (_sessions.Count == 0) DisposeMonitoring();
    }

    private void EnsureMonitoring()
    {
        if (_updates is not null) return;
        _updates = new DesktopLayerUpdateQueue(dispatcher, MaintainDesktop,
            exception => logger.Error(exception, "Plugin desktop layer update failed."));
        try { _monitor = new DesktopLayerMonitor(); _monitor.LayerChanged += _updates.Request; }
        catch (Exception exception) { logger.Error(exception, "Plugin desktop monitoring unavailable."); }
    }
    private void MaintainDesktop()
    {
        if (_closing) return;
        if (DesktopWindowLayer.IsEnabled)
            _desktopHost = DesktopWindowLayer.Maintain(_desktopHost, _sessions.Values.Select(s => s.Window.NativeHandle));
        else foreach (var session in _sessions.Values) session.Window.RecoverDesktop();
    }
    private void DisposeMonitoring()
    {
        if (_monitor is not null && _updates is not null) _monitor.LayerChanged -= _updates.Request;
        _monitor?.Dispose(); _monitor = null;
        _updates?.Dispose(); _updates = null;
    }

    private Task SaveLayoutAsync(string id, WidgetWindow window) =>
        service.SaveLayoutAsync(id, window.Left, window.Top,
            window.SizeToContent == SizeToContent.Manual ? window.Width : window.ActualWidth,
            window.SizeToContent == SizeToContent.Manual ? window.Height : window.ActualHeight);

    public async Task ShutdownAsync()
    {
        _closing = true;
        await _gate.WaitAsync();
        try
        {
            foreach (var id in _sessions.Keys.ToArray())
            {
                var window = _sessions[id].Window;
                try { await SaveLayoutAsync(id, window); }
                catch (Exception exception) { logger.Error(exception, "Plugin layout save failed on shutdown."); }
                Stop(id);
            }
        }
        finally { DisposeMonitoring(); _gate.Release(); }
    }
}
