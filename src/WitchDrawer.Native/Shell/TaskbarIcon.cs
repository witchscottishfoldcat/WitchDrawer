using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Shell;

public sealed class TaskbarIcon : IDisposable
{
    private readonly nint _windowHandle;
    private readonly uint _callbackMessageId;
    private readonly string _tooltip;
    private readonly nint _iconHandle;
    private readonly NativeWindow? _messageWindow;

    private bool _added;

    public event EventHandler? LeftClick;
    public event EventHandler? RightClick;
    public event EventHandler<MenuCommandEventArgs>? MenuCommand;

    public TaskbarIcon(nint ownerWindowHandle, string iconPath, string tooltip)
    {
        _windowHandle = ownerWindowHandle;
        _tooltip = tooltip;
        _callbackMessageId = NativeMethods.RegisterWindowMessageW("WitchDrawer.TaskbarIcon.Callback");

        _iconHandle = NativeMethods.LoadImage(
            nint.Zero,
            iconPath,
            NativeMethods.IMAGE_ICON,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON),
            NativeMethods.LR_LOADFROMFILE);

        if (_iconHandle == nint.Zero)
        {
            _iconHandle = NativeMethods.LoadIcon(nint.Zero, NativeMethods.IDI_APPLICATION);
        }

        _messageWindow = new NativeWindow(_callbackMessageId, this);
    }

    public void Show()
    {
        if (_added)
        {
            return;
        }

        var data = CreateNotifyData();
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data);
        _added = true;
    }

    public void Hide()
    {
        if (!_added)
        {
            return;
        }

        var data = CreateNotifyData();
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
        _added = false;
    }

    public void ShowContextMenu(nint menuHandle, int x, int y)
    {
        NativeMethods.SetForegroundWindow(_messageWindow?.Handle ?? _windowHandle);
        NativeMethods.TrackPopupMenu(menuHandle, NativeMethods.TPM_RIGHTALIGN, x, y, 0, _messageWindow?.Handle ?? _windowHandle, nint.Zero);
    }

    internal void OnCallback(nint wParam, nint lParam)
    {
        var message = lParam.ToInt32();
        switch (message)
        {
            case NativeMethods.WM_LBUTTONUP:
                LeftClick?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WM_RBUTTONUP:
                RightClick?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    internal void OnMenuCommand(int commandId)
    {
        MenuCommand?.Invoke(this, new MenuCommandEventArgs(commandId));
    }

    private NotifyIconData CreateNotifyData()
    {
        var hwnd = _messageWindow?.Handle ?? _windowHandle;
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = _callbackMessageId,
            hIcon = _iconHandle,
            szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        };

        return data;
    }

    public void Dispose()
    {
        Hide();

        if (_iconHandle != nint.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
        }

        _messageWindow?.Dispose();
        GC.SuppressFinalize(this);
    }

    ~TaskbarIcon()
    {
        Dispose();
    }

    private sealed class NativeWindow : IDisposable
    {
        private readonly uint _callbackMessageId;
        private readonly TaskbarIcon _owner;
        private readonly nint _hwnd;
        private readonly NativeMethods.WndProcDelegate _wndProc;
        private readonly ushort _classAtom;

        public nint Handle => _hwnd;

        public NativeWindow(uint callbackMessageId, TaskbarIcon owner)
        {
            _callbackMessageId = callbackMessageId;
            _owner = owner;

            var className = "WitchDrawerNotifyIcon_" + Guid.NewGuid().ToString("N");
            _wndProc = WndProc;

            var wc = new NativeMethods.WNDCLASS
            {
                lpfnWndProc = _wndProc,
                lpszClassName = className,
                hInstance = NativeMethods.GetModuleHandle(nint.Zero),
            };

            _classAtom = NativeMethods.RegisterClassW(ref wc);
            _hwnd = NativeMethods.CreateWindowExW(0, className, string.Empty, 0, 0, 0, 0, 0, nint.Zero, nint.Zero, wc.hInstance, nint.Zero);
        }

        private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == _callbackMessageId)
            {
                _owner.OnCallback(wParam, lParam);
                return nint.Zero;
            }

            if (msg == NativeMethods.WM_COMMAND)
            {
                var commandId = (int)(wParam.ToInt64() & 0xFFFF);
                _owner.OnMenuCommand(commandId);
                return nint.Zero;
            }

            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hwnd != nint.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
            }

            // The window class was registered per-instance; unregister it so repeated show/hide
            // cycles do not leak entries in the per-process atom table.
            if (_classAtom != 0)
            {
                NativeMethods.UnregisterClassW(_classAtom, NativeMethods.GetModuleHandle(nint.Zero));
            }

            GC.SuppressFinalize(this);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    private static class NativeMethods
    {
        public const int IMAGE_ICON = 1;
        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public const uint NIM_ADD = 0x00000000;
        public const uint NIM_DELETE = 0x00000002;
        public const uint NIF_MESSAGE = 0x00000001;
        public const uint NIF_ICON = 0x00000002;
        public const uint NIF_TIP = 0x00000004;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_COMMAND = 0x0111;
        public const uint TPM_RIGHTALIGN = 0x0008;

        public static readonly nint IDI_APPLICATION = new(32512);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        public static extern nint LoadIcon(nint hInstance, nint lpIconName);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(nint hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

        [DllImport("user32.dll")]
        public static extern nint DefWindowProcW(nint hWnd, uint Msg, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterClassW(ushort atom, nint hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(nint hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(nint lpModuleName);

        public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }
    }

    public sealed class MenuCommandEventArgs(int commandId) : EventArgs
    {
        public int CommandId { get; } = commandId;
    }
}
