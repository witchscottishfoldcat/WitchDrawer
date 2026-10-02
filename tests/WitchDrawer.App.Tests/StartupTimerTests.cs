using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Tests;

public sealed class StartupTimerTests
{
    [Fact]
    public void FirstMark_IncludesInitializationBeforeTheLoggerWasReady()
    {
        var logger = new RecordingLogger();
        var earlierTimestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        var timer = new StartupTimer(logger, earlierTimestamp);

        timer.Mark("Paths ready");

        var match = Regex.Match(logger.Message!, @"\+(\d+) ms \(total (\d+) ms\)");
        Assert.True(match.Success, logger.Message);
        Assert.True(long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) >= 1000);
        Assert.Equal(match.Groups[1].Value, match.Groups[2].Value);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public string? Message { get; private set; }

        public void Info(string message) => Message = message;

        public void Error(Exception exception, string message) => throw new NotSupportedException();
    }
}
