using System.Text;
using System.Threading.Channels;

namespace WitchDrawer.Core.Logging;

public sealed class FileAppLogger : IAppLogger, IAsyncDisposable
{
    private const int QueueCapacity = 1024;
    private const int MaximumMessageLength = 16384;
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly Channel<LogEntry> _queue = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private readonly Task _writer;
    private long _droppedEntries;

    public FileAppLogger(string logDirectory, int retentionDays = 7)
    {
        _logDirectory = logDirectory;
        _retentionDays = retentionDays;
        // All directory enumeration and disk writes belong to this worker.
        _writer = Task.Run(WriteLoopAsync);
    }

    public void Info(string message) => Enqueue("INFO", message);

    public void Error(Exception exception, string message)
    {
        try { Enqueue("ERROR", $"{message}{Environment.NewLine}{exception}"); }
        catch (Exception) { /* Even a failing exception formatter must not break error handling. */ }
    }

    private void Enqueue(string level, string message)
    {
        if (message.Length > MaximumMessageLength) message = message[..MaximumMessageLength] + " [truncated]";
        var timestamp = DateTimeOffset.Now;
        var entry = new LogEntry(timestamp, $"{timestamp:O} [{level}] {message}{Environment.NewLine}");
        // Producers never wait for the disk. Bound both queue length and entry size.
        if (!_queue.Writer.TryWrite(entry)) Interlocked.Increment(ref _droppedEntries);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _queue.Writer.WriteAsync(new LogEntry(default, null, completion), timeout.Token).ConfigureAwait(false);
            await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ChannelClosedException) { await WaitForWriterAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await WaitForWriterAsync().ConfigureAwait(false);
    }

    private async Task WaitForWriterAsync()
    {
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { /* Shutdown must not wait indefinitely for an unavailable disk. */ }
    }

    private async Task WriteLoopAsync()
    {
        TrimOldLogs();
        while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var batch = new StringBuilder();
            string? date = null;
            for (var count = 0; count < 64 && _queue.Reader.TryRead(out var entry); count++)
            {
                if (entry.Completion is not null)
                {
                    AppendWithoutThrowing(date, batch);
                    batch.Clear();
                    entry.Completion.TrySetResult();
                    continue;
                }

                var entryDate = entry.Timestamp.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (date != entryDate)
                {
                    AppendWithoutThrowing(date, batch);
                    batch.Clear();
                    date = entryDate;
                }
                var dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                if (dropped > 0) batch.AppendLine($"{entry.Timestamp:O} [WARN] Dropped {dropped} log entries because the queue was full or closed.");
                batch.Append(entry.Text);
            }
            AppendWithoutThrowing(date, batch);
        }
    }

    private void AppendWithoutThrowing(string? date, StringBuilder batch)
    {
        if (date is null || batch.Length == 0) return;
        try
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(Path.Combine(_logDirectory, date + ".log"), batch.ToString(), Encoding.UTF8);
        }
        catch (Exception) { /* Logging is best effort and must never affect application operations. */ }
    }

    private void TrimOldLogs()
    {
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-_retentionDays);
            if (!Directory.Exists(_logDirectory)) return;
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime) File.Delete(file);
        }
        catch (Exception) { }
    }

    private sealed record LogEntry(DateTimeOffset Timestamp, string? Text, TaskCompletionSource? Completion = null);
}

