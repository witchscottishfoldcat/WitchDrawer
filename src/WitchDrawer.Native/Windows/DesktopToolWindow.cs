using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Applies the native window behavior used by desktop boxes.
/// </summary>
public sealed class DesktopToolWindow
{
    public const int SystemCommandMessage = 0x0112;
    public const int WindowPositionChangedMessage = 0x0047;
    public const int SizeMessage = 0x0005;

    private const int WindowOwnerIndex = -8;
    private const int ExtendedStyleIndex = -20;
    private const nint ExtendedStyleAppWindow = 0x00040000;
    private const nint ExtendedStyleToolWindow = 0x00000080;
    private const nint ExtendedStyleNoActivate = 0x08000000;
    private const nint ExtendedStyleTopmost = 0x00000008;
    private const uint GetWindowPrevious = 3;
    private const uint GetWindowOwner = 4;
    private const nint SystemCommandMask = 0xFFF0;
    private const nint SystemCommandMinimize = 0xF020;

    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const uint SetWindowPositionShowWindow = 0x0040;
    private const int ShowWithoutActivation = 4;

    private readonly nint _handle;

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

    public static int TaskbarCreatedMessage { get; } =
        unchecked((int)RegisterWindowMessageW("TaskbarCreated"));

    /// <summary>
    /// Keeps the WPF HWND as an unowned top-level tool window. The window style
    /// is intentionally left alone because changing it after WPF creates the
    /// HWND can make WPF assign an internal hidden owner.
    /// </summary>
    public void Configure()
    {
        var extendedStyle = GetWindowLongPtr(_handle, ExtendedStyleIndex);
        extendedStyle |= ExtendedStyleToolWindow | ExtendedStyleNoActivate;
        extendedStyle &= ~ExtendedStyleAppWindow;
        SetWindowLongPtr(_handle, ExtendedStyleIndex, extendedStyle);
        ClearOwner(_handle);
        SetWindowPos(
            _handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove
            | SetWindowPositionNoSize
            | SetWindowPositionNoZOrder
            | SetWindowPositionNoActivate
            | SetWindowPositionFrameChanged);
    }

    /// <summary>
    /// Restores visible desktop boxes to the narrow Z-order band immediately
    /// above Explorer's desktop host and below ordinary application windows.
    /// </summary>
    /// <returns>The validated or newly resolved desktop host.</returns>
    public static nint MaintainDesktopLayer(
        nint cachedDesktopHost,
        IEnumerable<nint> windowHandles)
    {
        var desktopHost = IsWindow(cachedDesktopHost)
            && ContainsChildWindowClass(cachedDesktopHost, "SHELLDLL_DefView")
            ? cachedDesktopHost
            : FindDesktopHost();
        if (desktopHost == nint.Zero)
        {
            return nint.Zero;
        }

        var anchor = desktopHost;
        foreach (var windowHandle in windowHandles)
        {
            if (windowHandle == nint.Zero || !IsWindow(windowHandle))
            {
                continue;
            }

            ClearOwner(windowHandle);
            if (IsIconic(windowHandle) || !IsWindowVisible(windowHandle))
            {
                ShowWindow(windowHandle, ShowWithoutActivation);
            }

            var windowAboveAnchor = GetWindow(anchor, GetWindowPrevious);
            if (windowAboveAnchor != windowHandle)
            {
                // An insertion after a topmost predecessor would promote the box.
                var insertAfter = windowAboveAnchor != nint.Zero
                    && (GetWindowLongPtr(windowAboveAnchor, ExtendedStyleIndex) & ExtendedStyleTopmost) != 0
                    ? (nint)(-2) // HWND_NOTOPMOST: top of the non-topmost band.
                    : windowAboveAnchor;
                SetWindowPos(
                    windowHandle,
                    insertAfter,
                    0,
                    0,
                    0,
                    0,
                    SetWindowPositionNoMove
                    | SetWindowPositionNoSize
                    | SetWindowPositionNoActivate);
            }

            anchor = windowHandle;
        }

        return desktopHost;
    }

    /// <summary>
    /// Finds the WorkerW that owns SHELLDLL_DefView, which is the desktop icon
    /// host used by Show Desktop. Progman is retained only as a shell fallback.
    /// </summary>
    public static nint FindDesktopHost()
    {
        nint desktopHost = nint.Zero;
        EnumWindows(
            (windowHandle, _) =>
            {
                var className = GetWindowClassName(windowHandle);
                var containsShellDefView = string.Equals(className, "WorkerW", StringComparison.Ordinal)
                    && ContainsChildWindowClass(windowHandle, "SHELLDLL_DefView");
                if (!IsDesktopHostCandidate(className, containsShellDefView))
                {
                    return true;
                }

                desktopHost = windowHandle;
                return false;
            },
            nint.Zero);

        return desktopHost != nint.Zero
            ? desktopHost
            : FindWindowW("Progman", null);
    }

    internal static bool IsDesktopHostCandidate(
        string? className,
        bool containsShellDefView) =>
        string.Equals(className, "WorkerW", StringComparison.Ordinal)
        && containsShellDefView;

    public static bool IsMinimizeSystemCommand(int message, nint command)
    {
        return message == SystemCommandMessage
            && (command & SystemCommandMask) == SystemCommandMinimize;
    }

    public static bool IsDesktopLayerChangeMessage(int message, nint wordParameter, nint longParameter)
    {
        if (message == SizeMessage)
        {
            return wordParameter == 1; // SIZE_MINIMIZED, including direct ShowWindow calls.
        }

        return message == WindowPositionChangedMessage
            && longParameter != nint.Zero
            && AffectsDesktopLayer(Marshal.PtrToStructure<WindowPosition>(longParameter).Flags);
    }

    internal static bool AffectsDesktopLayer(uint positionFlags) =>
        (positionFlags & SetWindowPositionNoZOrder) == 0
        || (positionFlags & SetWindowPositionShowWindow) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public nint Window;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    private static void ClearOwner(nint windowHandle)
    {
        if (GetWindow(windowHandle, GetWindowOwner) != nint.Zero)
        {
            SetWindowLongPtr(windowHandle, WindowOwnerIndex, nint.Zero);
        }
    }

    private static bool ContainsChildWindowClass(nint parentHandle, string expectedClassName)
    {
        var found = false;
        EnumChildWindows(
            parentHandle,
            (windowHandle, _) =>
            {
                if (!string.Equals(
                        GetWindowClassName(windowHandle),
                        expectedClassName,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                found = true;
                return false;
            },
            nint.Zero);
        return found;
    }

    private static string? GetWindowClassName(nint windowHandle)
    {
        var className = new StringBuilder(64);
        return GetClassNameW(windowHandle, className, className.Capacity) > 0
            ? className.ToString()
            : null;
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

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        nint parentHandle,
        EnumWindowsCallback callback,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

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
