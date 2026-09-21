using System.Diagnostics;
using System.Windows.Threading;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 启动期间的界面线程卡顿监控：DispatcherTimer 以固定间隔触发，
/// 若实际触发间隔明显超过预期，说明界面线程被同步工作（磁盘 IO、
/// 数据库读取、图标提取等）阻塞，记录阻塞时长用于定位卡顿来源。
/// 仅在启动阶段启用，启动完成后释放。
/// </summary>
internal sealed class UiThreadStallMonitor : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly double _thresholdMs;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private int _stallCount;
    private double _worstStallMs;

    public UiThreadStallMonitor(
        IAppLogger logger,
        double intervalMs = 50,
        double thresholdMs = 150)
    {
        _logger = logger;
        _thresholdMs = thresholdMs;
        _lastTick = _stopwatch.Elapsed;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _stopwatch.Elapsed;
        var gapMs = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;
        if (gapMs <= _thresholdMs)
        {
            return;
        }

        _stallCount++;
        _worstStallMs = Math.Max(_worstStallMs, gapMs);
        _logger.Info($"[startup] UI thread stalled {gapMs:F0} ms");
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        if (_stallCount > 0)
        {
            _logger.Info(
                $"[startup] UI thread stalls during startup: {_stallCount}, worst {_worstStallMs:F0} ms");
        }
    }
}
