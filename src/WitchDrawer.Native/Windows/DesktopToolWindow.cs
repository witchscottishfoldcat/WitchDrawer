using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Applies the native window behavior used by desktop boxes.
/// </summary>
public sealed class DesktopToolWindow
{
    public const int SystemCommandMessage = 0x0112;

    private const int WindowOwnerIndex = -8;
    private const int ExtendedStyleIndex = -20;
    private const nint ExtendedStyleAppWindow = 0x00040000;
    private const nint ExtendedStyleToolWindow = 0x00000080;
    private const nint ExtendedStyleNoActivate = 0x08000000;
    private const uint GetWindowOwner = 4;
    private const nint SystemCommandMask = 0xFFF0;
    private const nint SystemCommandMinimize = 0xF020;

    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const int ShowWithoutActivation = 4;

    private static readonly nint WindowPositionTopMost = -1;
    private static readonly nint WindowPositionNotTopMost = -2;
    private static readonly nint WindowPositionBottom = 1;

    private readonly nint _handle;
    private nint _originalOwner;
    private nint _desktopOwner;

    public DesktopToolWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(handle));
        }

        _handle = handle;
    }

    public nint Handle => _handle;

    public bool IsAlive => IsWindow(_handle);

    public bool IsDesktopHosted =>
        _desktopOwner != nint.Zero
        && IsWindow(_desktopOwner)
        && GetWindow(_handle, GetWindowOwner) == _desktopOwner;

    public static int TaskbarCreatedMessage { get; } =
        unchecked((int)RegisterWindowMessageW("TaskbarCreated"));

    /// <summary>
    /// Marks the window as a tool window so Windows excludes it from Alt+Tab,
    /// attaches it to the desktop shell when available, and puts it at the
    /// bottom of the normal top-level window Z order. WS_EX_NOACTIVATE 阻止
    /// 点击激活：点击不再触发 Windows 的默认"激活并抬升"，盒子不会在点击瞬间
    /// 浮出其他窗口再被压回（消除点击闪帧）。程序化 Activate() 不受此限制。
    /// </summary>
    public void Configure()
    {
        var extendedStyle = GetWindowLongPtr(_handle, ExtendedStyleIndex);
        extendedStyle |= ExtendedStyleToolWindow | ExtendedStyleNoActivate;
        extendedStyle &= ~ExtendedStyleAppWindow;
        SetWindowLongPtr(_handle, ExtendedStyleIndex, extendedStyle);

        TryAttachToDesktop();
        SendToBottom();
    }

    /// <summary>
    /// Makes the Shell desktop the owner of this top-level WPF window. An owned
    /// window stays above its owner without entering the system-wide topmost
    /// band, so Show Desktop no longer requires a visible Z-order correction.
    ///
    /// A true WorkerW child is not used here: cross-process SetParent changes
    /// the WPF window's DPI hosting context and transparent WPF child windows
    /// are not composed reliably by Explorer. Ownership keeps the HWND top-level
    /// and preserves WPF rendering, input and per-monitor DPI behavior.
    /// </summary>
    public bool TryAttachToDesktop()
    {
        if (!IsAlive)
        {
            _desktopOwner = nint.Zero;
            return false;
        }

        var shellWindow = GetShellWindow();
        if (shellWindow == nint.Zero || !IsWindow(shellWindow))
        {
            RestoreOriginalOwner();
            return false;
        }

        var currentOwner = GetWindow(_handle, GetWindowOwner);
        if (currentOwner == shellWindow)
        {
            _desktopOwner = shellWindow;
            return true;
        }

        if (_originalOwner == nint.Zero
            && currentOwner != nint.Zero
            && IsWindow(currentOwner))
        {
            _originalOwner = currentOwner;
        }

        SetWindowLongPtr(_handle, WindowOwnerIndex, shellWindow);
        if (GetWindow(_handle, GetWindowOwner) != shellWindow)
        {
            RestoreOriginalOwner();
            return false;
        }

        _desktopOwner = shellWindow;
        return true;
    }

    public void SendToBottom()
    {
        SetWindowPos(
            _handle,
            WindowPositionBottom,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove
            | SetWindowPositionNoSize
            | SetWindowPositionNoActivate
            | SetWindowPositionFrameChanged);
    }

    /// <summary>
    /// Raises the box above the Shell desktop without leaving it topmost.
    /// Show Desktop puts Progman/WorkerW ahead of normal windows, so a plain
    /// HWND_TOP/NOTOPMOST request can be ignored. A synchronous topmost pulse
    /// crosses that Shell boundary, and the second call immediately returns
    /// the box to the normal band before the compositor presents a frame.
    /// </summary>
    public void BringAboveDesktop()
    {
        if (IsDesktopHosted)
        {
            return;
        }

        SetWindowPos(
            _handle,
            WindowPositionTopMost,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove | SetWindowPositionNoSize | SetWindowPositionNoActivate);

        SetWindowPos(
            _handle,
            WindowPositionNotTopMost,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove | SetWindowPositionNoSize | SetWindowPositionNoActivate);
    }

    private void RestoreOriginalOwner()
    {
        _desktopOwner = nint.Zero;
        if (_originalOwner != nint.Zero && IsWindow(_originalOwner))
        {
            SetWindowLongPtr(_handle, WindowOwnerIndex, _originalOwner);
        }
    }

    /// <summary>
    /// Restores a window minimized directly by the shell without activating it.
    /// This is the fallback for Show Desktop implementations which bypass
    /// <see cref="SystemCommandMessage"/>.
    /// </summary>
    public void RestoreWithoutActivation()
    {
        ShowWindow(_handle, ShowWithoutActivation);
    }

    public static bool IsMinimizeSystemCommand(int message, nint command)
    {
        return message == SystemCommandMessage
            && (command & SystemCommandMask) == SystemCommandMinimize;
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index)
    {
        return nint.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : GetWindowLong32(windowHandle, index);
    }

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value)
    {
        return nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : SetWindowLong32(windowHandle, index, value);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string messageName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

}
