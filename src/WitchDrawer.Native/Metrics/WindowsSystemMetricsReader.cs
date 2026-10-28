using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WitchDrawer.Native.Metrics;

internal readonly record struct SystemMetricsSnapshot(double? CpuPercent, ulong TotalMemoryBytes, ulong AvailableMemoryBytes)
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public ulong UsedMemoryBytes => TotalMemoryBytes - Math.Min(AvailableMemoryBytes, TotalMemoryBytes);
    public double MemoryPercent => TotalMemoryBytes == 0 ? 0 : 100.0 * UsedMemoryBytes / TotalMemoryBytes;
}

/// <summary>Call only on a worker. Has no timer, WMI, managed package dependency or administrator requirement.</summary>
internal sealed class WindowsSystemMetricsReader : IDisposable
{
    private SafePdhQuery? _query;
    private nint _counter;
    private bool _hasBaseline;
    private bool _disposed;

    public WindowsSystemMetricsReader()
    {
        // Processor Information(_Total) includes all processor groups. English counter paths work
        // on localized Windows. A missing/disabled counter leaves RAM monitoring available.
        if (PdhOpenQueryW(null, 0, out var query) == 0)
        {
            _query = query;
            if (PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\% Processor Time", 0, out _counter) != 0)
            {
                query.Dispose();
                _query = null;
            }
        }
        else query?.Dispose();
    }

    public SystemMetricsSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        double? cpu = null;
        if (_query is not null)
        {
            var collected = PdhCollectQueryData(_query) == 0;
            if (collected && _hasBaseline && PdhGetFormattedCounterValue(_counter, 0x200, out _, out var value) == 0)
                cpu = NormalizeCpu(value.Status, value.Value);
            _hasBaseline = collected;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new SystemMetricsSnapshot(cpu, memory.TotalPhysical, memory.AvailablePhysical);
    }

    internal static double? NormalizeCpu(uint status, double value) =>
        status is 0 or 1 && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query?.Dispose();
        _query = null;
    }

    private sealed class SafePdhQuery : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQuery() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, nuint userData, out SafePdhQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(SafePdhQuery query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(SafePdhQuery query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out FormattedCounterValue value);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
