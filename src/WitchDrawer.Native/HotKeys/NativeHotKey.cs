using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WitchDrawer.Native.HotKeys;

public sealed class NativeHotKey : IDisposable
{
    private readonly nint _windowHandle;
    private readonly int _id;
    private bool _registered;

    public NativeHotKey(nint windowHandle, int id)
    {
        _windowHandle = windowHandle;
        _id = id;
    }

    public int Id => _id;

    public void Register(HotKeyModifiers modifiers, uint virtualKey)
    {
        Unregister();

        if (!RegisterHotKey(_windowHandle, _id, (uint)modifiers, virtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register global hotkey.");
        }

        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_windowHandle, _id);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}

