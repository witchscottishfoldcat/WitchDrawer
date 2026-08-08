using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Reports system-wide mouse button presses via WH_MOUSE_LL without polling.
/// The callback runs on the installing thread's message loop, so construct this
/// on the UI thread.
///
/// Desktop boxes carry WS_EX_NOACTIVATE: clicking one never activates the window
/// or the app, so neither Window.Deactivated nor Application.Deactivated fires
/// when the user then clicks the desktop. A low-level mouse hook is the only
/// reliable "click landed outside this box" signal left. Cost per button press
/// is a single hit-test plus one comparison per box; there is no polling and no
/// extra thread, so background CPU stays at zero while the mouse is idle.
/// </summary>
public sealed class GlobalMouseButtonMonitor : IDisposable
{
    private const int LowLevelMouseHook = 14; // WH_MOUSE_LL

    private const int LeftButtonDown = 0x0201; // WM_LBUTTONDOWN
    private const int NonClientLeftButtonDown = 0x00A1; // WM_NCLBUTTONDOWN
    private const int RightButtonDown = 0x0204; // WM_RBUTTONDOWN
    private const int NonClientRightButtonDown = 0x00A4; // WM_NCRBUTTONDOWN
    private const int MiddleButtonDown = 0x0207; // WM_MBUTTONDOWN
    private const int NonClientMiddleButtonDown = 0x00A7; // WM_NCMBUTTONDOWN

    private readonly HookCallback _callback;
    private nint _hook;

    public GlobalMouseButtonMonitor()
    {
        _callback = OnHookCallback;
        _hook = SetWindowsHookEx(
            LowLevelMouseHook,
            _callback,
            GetModuleHandle(null),
            0);
    }

    /// <summary>
    /// Raised on the installing thread for every system-wide button press.
    /// Parameters are screen coordinates in physical pixels.
    /// </summary>
    public event Action<int, int>? MouseButtonDown;

    public bool IsActive => _hook != nint.Zero;

    /// <summary>
    /// Returns the handle of the window under the given screen point
    /// (physical pixels), or zero when no window is hit.
    /// </summary>
    public static nint HitTestWindowHandle(int screenX, int screenY)
    {
        return WindowFromPoint(new NativePoint { X = screenX, Y = screenY });
    }

    public void Dispose()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
        GC.SuppressFinalize(this);
    }

    private static bool IsButtonDownMessage(int message)
    {
        return message is LeftButtonDown
            or NonClientLeftButtonDown
            or RightButtonDown
            or NonClientRightButtonDown
            or MiddleButtonDown
            or NonClientMiddleButtonDown;
    }

    private nint OnHookCallback(int code, nint wordParameter, nint longParameter)
    {
        if (code >= 0 && IsButtonDownMessage((int)wordParameter))
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseHookStruct>(longParameter);
                MouseButtonDown?.Invoke(data.X, data.Y);
            }
            catch
            {
                // Exceptions must never escape a native callback boundary.
            }
        }

        return CallNextHookEx(_hook, code, wordParameter, longParameter);
    }

    private delegate nint HookCallback(int code, nint wordParameter, nint longParameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public int X;
        public int Y;
        public int MouseData;
        public int Flags;
        public int Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookType,
        HookCallback callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
