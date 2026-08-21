using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 包装刻意不等待的异步任务：异常会被记录而不是成为无人观察的任务异常。
/// 此前 <c>_ = SomeAsync()</c> 的写法在 SQLite 等失败时会静默吞掉错误，连日志都没有。
/// </summary>
internal static class FireAndForget
{
    public static void Run(Task task, IAppLogger logger, string context)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(logger);
        _ = ObserveAsync(task, logger, context);
    }

    private static async Task ObserveAsync(Task task, IAppLogger logger, string context)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Error(exception, context);
        }
    }
}
