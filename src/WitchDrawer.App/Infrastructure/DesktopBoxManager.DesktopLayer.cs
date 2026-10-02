using System.Windows;
using System.Windows.Threading;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    private DesktopLayerMonitor? _desktopLayerMonitor;
    private DesktopLayerUpdateQueue? _desktopLayerUpdates;
    private nint _desktopLayerHost;
    private bool _maintainingDesktopLayer;

    private void InitializeDesktopLayer()
    {
        _desktopLayerUpdates = new DesktopLayerUpdateQueue(
            Dispatcher.CurrentDispatcher, MaintainDesktopLayer,
            exception => _logger.Error(exception, "Failed to process desktop layer event."));
        _desktopLayerMonitor = new DesktopLayerMonitor();
        _desktopLayerMonitor.LayerChanged += QueueDesktopLayerUpdate;
    }

    private void QueueDesktopLayerUpdate() => _desktopLayerUpdates?.Request();

    internal static T[] SnapshotDesktopLayerWindows<T>(
        IEnumerable<T> windows, Func<T, Visibility> visibility, Func<T, bool> isAlive) =>
        windows.Where(window => visibility(window) == Visibility.Visible && isAlive(window)).ToArray();

    private void MaintainDesktopLayer()
    {
        // Both the queue and the explicit startup call run on the UI thread.
        // Do not inspect WPF state or the dictionary in a native callback.
        if (!DesktopWindowLayer.IsEnabled || _closing || _maintainingDesktopLayer)
        {
            return;
        }

        var handles = SnapshotDesktopLayerWindows(
                _windows.Values, window => window.Visibility, window => window.IsNativeWindowAlive)
            .Select(window => window.NativeHandle).ToArray();
        if (handles.Length == 0)
        {
            return;
        }

        _maintainingDesktopLayer = true;
        try
        {
            var previousHost = _desktopLayerHost;
            _desktopLayerHost = DesktopWindowLayer.Maintain(_desktopLayerHost, handles);
            if (_desktopLayerHost != 0 && _desktopLayerHost != previousHost)
            {
                _logger.Info($"Event-driven Windows 11 desktop layer: host=0x{_desktopLayerHost:X}.");
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to maintain desktop layer.");
        }
        finally
        {
            _maintainingDesktopLayer = false;
        }
    }

    private void DisposeDesktopLayer()
    {
        if (_desktopLayerMonitor is not null)
        {
            _desktopLayerMonitor.LayerChanged -= QueueDesktopLayerUpdate;
            _desktopLayerMonitor.Dispose();
        }
        _desktopLayerUpdates?.Dispose();
    }
}
