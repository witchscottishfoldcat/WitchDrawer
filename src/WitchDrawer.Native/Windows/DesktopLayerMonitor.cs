using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Observes desktop Z-order changes without timers or keyboard hooks.
/// Create and dispose on the thread that pumps the window message loop.
/// </summary>
public sealed class DesktopLayerMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectReorder = 0x8004;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private readonly WinEventCallback _callback;
    private readonly nint _desktopWindow = GetDesktopWindow();
    private nint _foregroundHook;
    private nint _desktopHook;
    private bool _disposed;

    public DesktopLayerMonitor()
    {
        _callback = OnWinEvent;
        _foregroundHook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, nint.Zero,
            _callback, 0, 0, WinEventSkipOwnProcess);
        _desktopHook = SetWinEventHook(
            EventObjectCreate, EventObjectReorder, nint.Zero,
            _callback, 0, 0, WinEventSkipOwnProcess);
        if (_foregroundHook == nint.Zero || _desktopHook == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Unable to monitor desktop window events.");
        }
    }

    public event Action? LayerChanged;

    private void OnWinEvent(
        nint hook, uint eventType, nint windowHandle,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_disposed || windowHandle == nint.Zero || !IsWindowObject(objectId, childId))
        {
            return;
        }

        // Desktop-window REORDER covers changes that finish after FOREGROUND.
        // Ignore control/list accessibility events and our own SetWindowPos calls.
        var className = new StringBuilder(64);
        if (eventType != EventSystemForeground && windowHandle != _desktopWindow)
        {
            GetClassNameW(windowHandle, className, className.Capacity);
        }

        if (IsRelevantEvent(eventType, windowHandle == _desktopWindow, className.ToString()))
        {
            try
            {
                LayerChanged?.Invoke();
            }
            catch (Exception exception)
            {
                // Exceptions must never escape a native callback boundary.
                System.Diagnostics.Trace.TraceError($"Desktop layer event failed: {exception}");
            }
        }
    }

    // REORDER can describe the desktop's client area rather than OBJID_WINDOW.
    internal static bool IsWindowObject(int objectId, int childId) =>
        childId == 0 && objectId is 0 or -4;

    internal static bool IsRelevantEvent(uint eventType, bool isDesktopWindow, string className) =>
        eventType == EventSystemForeground
        || (eventType >= EventObjectCreate && eventType <= EventObjectReorder
            && (isDesktopWindow || className is "WorkerW" or "Progman" or "SHELLDLL_DefView"));

    public void Dispose()
    {
        _disposed = true;
        if (_foregroundHook != nint.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = nint.Zero;
        }

        if (_desktopHook != nint.Zero)
        {
            UnhookWinEvent(_desktopHook);
            _desktopHook = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private delegate void WinEventCallback(
        nint hook, uint eventType, nint windowHandle,
        int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, WinEventCallback callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint handle, StringBuilder className, int maximumCount);
}
