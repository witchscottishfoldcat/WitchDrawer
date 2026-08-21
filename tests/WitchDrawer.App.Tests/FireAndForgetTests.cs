using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Tests;

public sealed class FireAndForgetTests
{
    [Fact]
    public async Task Run_LogsFaultedTaskExceptionInsteadOfLeavingItUnobserved()
    {
        var logger = new RecordingLogger();

        FireAndForget.Run(
            Task.FromException(new InvalidOperationException("boom")),
            logger,
            "test context");

        var error = await logger.WaitForErrorAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("test context", error.Message);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Run_DoesNotLogForSuccessfulTasks()
    {
        var logger = new RecordingLogger();

        FireAndForget.Run(Task.CompletedTask, logger, "test context");
        await Task.Delay(200);

        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Run_RejectsNullArguments()
    {
        var logger = new RecordingLogger();

        Assert.Throws<ArgumentNullException>(() => FireAndForget.Run(null!, logger, "ctx"));
        Assert.Throws<ArgumentNullException>(() => FireAndForget.Run(Task.CompletedTask, null!, "ctx"));
    }

    [Fact]
    public async Task ObserveAsync_DoesNotFaultWhenTheLoggerFails()
    {
        await FireAndForget.ObserveAsync(
            Task.FromException(new InvalidOperationException("operation failed")),
            new ThrowingLogger(),
            "test context");
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private readonly TaskCompletionSource<(Exception Exception, string Message)> _firstError =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(Exception Exception, string Message)> Errors { get; } = [];

        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
            lock (Errors)
            {
                Errors.Add((exception, message));
            }

            _firstError.TrySetResult((exception, message));
        }

        public async Task<(Exception Exception, string Message)> WaitForErrorAsync(TimeSpan timeout)
        {
            return await _firstError.Task.WaitAsync(timeout);
        }
    }

    private sealed class ThrowingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
            throw new IOException("log write failed");
        }
    }
}
