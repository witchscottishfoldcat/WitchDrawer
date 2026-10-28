using WitchDrawer.Native.Metrics;

namespace WitchDrawer.Native.Tests;

public sealed class SystemMetricsReaderTests
{
    [Theory]
    [InlineData(16UL, 4UL, 12UL, 75.0)]
    [InlineData(16UL, 20UL, 0UL, 0.0)]
    [InlineData(0UL, 0UL, 0UL, 0.0)]
    public void MemoryUsageUsesPhysicalAvailableBytesWithoutUnderflow(ulong total, ulong available, ulong used, double percentage)
    {
        var snapshot = new SystemMetricsSnapshot(null, total, available);
        Assert.Equal(used, snapshot.UsedMemoryBytes);
        Assert.Equal(percentage, snapshot.MemoryPercent);
    }

    [Theory]
    [InlineData(0U, 42.5, 42.5)]
    [InlineData(1U, 120.0, 100.0)]
    [InlineData(0U, -0.1, 0.0)]
    public void CpuAcceptsOnlyValidFiniteCounterResults(uint status, double input, double expected) =>
        Assert.Equal(expected, WindowsSystemMetricsReader.NormalizeCpu(status, input));

    [Fact]
    public void CpuRejectsUnavailableAndNonFiniteData()
    {
        Assert.Null(WindowsSystemMetricsReader.NormalizeCpu(0xC0000BC6, 0));
        Assert.Null(WindowsSystemMetricsReader.NormalizeCpu(0, double.NaN));
        Assert.Null(WindowsSystemMetricsReader.NormalizeCpu(1, double.PositiveInfinity));
    }

    [Fact]
    public async Task WindowsReaderWarmsUpWithoutInventingZeroAndClosesItsQuery()
    {
        await Task.Run(async () =>
        {
            var reader = new WindowsSystemMetricsReader();
            try
            {
                var first = reader.Read();
                Assert.Null(first.CpuPercent);
                Assert.True(first.TotalMemoryBytes > 0);
                Assert.InRange(first.AvailableMemoryBytes, 0UL, first.TotalMemoryBytes);
                await Task.Delay(1100);
                var next = reader.Read();
                if (next.CpuPercent is { } cpu) Assert.InRange(cpu, 0, 100);
                Assert.InRange(next.MemoryPercent, 0, 100);
            }
            finally { reader.Dispose(); }
            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.Read());
        });
    }
}
