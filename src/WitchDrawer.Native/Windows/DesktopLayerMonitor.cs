using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

/// <summary>Desktop window events. Construct/dispose on the UI message-loop thread.</summary>
public sealed class DesktopLayerMonitor : IDisposable
{
    private const uint Foreground = 0x0003;
    private const uint Create = 0x8000;
    private const uint Reorder = 0x8004;
    private const uint SkipOwnProcess = 0x0002;
    private readonly WinEventCallback _callback;
    private readonly nint _desktop = GetDesktopWindow();
    private nint _foregroundHook;
    private nint _desktopHook;
    private bool _disposed;

    public DesktopLayerMonitor()
    {
        _callback = OnEvent;
        // OUTOFCONTEXT (0): callbacks are delivered on the installing thread.
        // Skip our own SetWindowPos notifications to avoid feedback.
        _foregroundHook = SetWinEventHook(Foreground, Foreground, 0, _callback, 0, 0, SkipOwnProcess);
        _desktopHook = SetWinEventHook(Create, Reorder, 0, _callback, 0, 0, SkipOwnProcess);
        if (_foregroundHook == 0 || _desktopHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Unable to monitor desktop window events.");
        }
    }

    public event Action? LayerChanged;

    private void OnEvent(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            if (_disposed || window == 0 || !IsWindowObject(objectId, childId))
            {
                return;
            }
            var name = new StringBuilder(64);
            if (type != Foreground && window != _desktop)
            {
                GetClassNameW(window, name, name.Capacity);
            }
            if (IsRelevantEvent(type, window == _desktop, name.ToString()))
            {
                LayerChanged?.Invoke();
            }
        }
        catch (Exception exception)
        {
            // Never propagate managed exceptions through a native callback.
            System.Diagnostics.Trace.TraceError($"Desktop layer callback failed: {exception}");
        }
    }

    internal static bool IsWindowObject(int objectId, int childId) =>
        childId == 0 && objectId is 0 or -4; // OBJID_WINDOW / OBJID_CLIENT

    internal static bool IsRelevantEvent(uint type, bool desktop, string className) =>
        type == Foreground || (type >= Create && type <= Reorder
            && (desktop || className is "WorkerW" or "Progman" or "SHELLDLL_DefView"));

    public void Dispose()
    {
        _disposed = true;
        if (_foregroundHook != 0)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }
        if (_desktopHook != 0)
        {
            UnhookWinEvent(_desktopHook);
            _desktopHook = 0;
        }
        GC.SuppressFinalize(this);
    }

    private delegate void WinEventCallback(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder text, int length);
}
