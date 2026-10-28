using System.Windows;
using System.Globalization;

namespace WitchDrawer.PluginSdk;

/// <summary>
/// UI factories and Dispose run on the WPF dispatcher and must be quick.
/// Start collection on worker threads; dispose timers/tasks/subscriptions on disable.
/// API v1 hosts one widget instance per enabled plugin.
/// </summary>
public interface IWidgetPlugin : IDisposable
{
    FrameworkElement CreateView(WidgetContext context);
    FrameworkElement? CreateSettingsView() => null;
}

/// <summary>Declared capabilities are informational; in-process plugins are trusted code, not sandboxed.</summary>
public sealed class WidgetContext(
    IReadOnlyDictionary<string, string> settings,
    Func<IReadOnlyDictionary<string, string>, Task> saveSettingsAsync,
    Action<Exception> reportError,
    Func<CultureInfo>? getCulture = null)
{
    public CultureInfo Culture => getCulture?.Invoke() ?? CultureInfo.CurrentCulture;
    public IReadOnlyDictionary<string, string> Settings { get; } = settings;
    public Task SaveSettingsAsync(IReadOnlyDictionary<string, string> values) => saveSettingsAsync(values);
    public void ReportError(Exception exception) => reportError(exception);
}
