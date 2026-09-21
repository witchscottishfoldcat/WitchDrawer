using System.Diagnostics;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 启动阶段计时：逐阶段记录增量与累计耗时到日志，用于对比
/// "窗口出现慢"和"出现后操作卡"，以及首次/再次启动、盒子多少的差异。
/// </summary>
internal sealed class StartupTimer
{
    private readonly IAppLogger _logger;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastMark;

    public StartupTimer(IAppLogger logger)
    {
        _logger = logger;
    }

    public void Mark(string phase)
    {
        var now = _stopwatch.Elapsed;
        _logger.Info(
            $"[startup] {phase}: +{(now - _lastMark).TotalMilliseconds:F0} ms "
            + $"(total {now.TotalMilliseconds:F0} ms)");
        _lastMark = now;
    }
}
