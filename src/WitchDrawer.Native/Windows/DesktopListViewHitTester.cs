using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

internal interface IDesktopHitTestSession : IDisposable
{
    bool IsBlankAt(int screenX, int screenY);
}

/// <summary>
/// One on-demand background query, with no polling, queue, or resident worker.
/// A slow accessibility provider keeps ownership of its COM session until it
/// returns; timing out a caller never releases resources underneath that query.
/// </summary>
internal sealed class DesktopListViewHitTester(
    Func<nint, IDesktopHitTestSession?> openSession, TimeSpan? timeout = null)
{
    private readonly object _sync = new();
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMilliseconds(100);
    private Task<bool>? _pending;

    internal async Task<bool> IsBlankAsync(nint listView, int screenX, int screenY)
    {
        Task<bool> operation;
        lock (_sync)
        {
            // A hung provider must not accumulate worker threads or requests.
            if (_pending is { IsCompleted: false }) return false;
            operation = _pending = Task.Run(() =>
            {
                try
                {
                    using var session = openSession(listView);
                    return session?.IsBlankAt(screenX, screenY) == true;
                }
                catch (Exception)
                {
                    // Explorer can restart or stop exposing the object mid-query.
                    // An unknown point must never toggle the user's desktop icons.
                    return false;
                }
            });
        }

        try { return await operation.WaitAsync(_timeout).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }
}

/// <summary>MSAA supplies marshaling; no memory is allocated or written in Explorer.</summary>
internal static class AccessibleDesktopHitTest
{
    internal static IDesktopHitTestSession? OpenSession(nint window)
    {
        // The query is entirely synchronous inside its pool worker, so the COM
        // apartment and every returned interface share one well-defined lifetime.
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0)); // COINIT_MULTITHREADED
        IAccessibleHitTest? accessible = null;
        var sessionOwnsApartment = false;
        try
        {
            var iid = typeof(IAccessibleHitTest).GUID;
            var result = AccessibleObjectFromWindow(window, unchecked((uint)-4), ref iid, out accessible);
            if (result >= 0 && accessible is not null)
            {
                var session = new Session(accessible);
                sessionOwnsApartment = true;
                return session;
            }
            return null;
        }
        finally
        {
            if (!sessionOwnsApartment)
            {
                try { Release(accessible); }
                finally { CoUninitialize(); }
            }
        }
    }

    // CHILDID_SELF: inside the list, but outside all of its child icons.
    internal static bool IsBlankResult(object? result) => result is int childId && childId == 0;

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private sealed class Session(IAccessibleHitTest accessible) : IDesktopHitTestSession
    {
        private IAccessibleHitTest? _accessible = accessible;

        public bool IsBlankAt(int screenX, int screenY)
        {
            object? hit = null;
            try
            {
                hit = _accessible!.HitTest(screenX, screenY);
                return IsBlankResult(hit);
            }
            finally { Release(hit); } // A hit child can itself be an IAccessible RCW.
        }

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _accessible, null);
            if (value is null) return;
            try { Release(value); }
            finally { CoUninitialize(); }
        }
    }

    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessibleHitTest
    {
        [DispId(-5017)] // DISPID_ACC_HITTEST
        [return: MarshalAs(UnmanagedType.Struct)]
        object? HitTest(int screenX, int screenY);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint window, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleHitTest? accessible);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
