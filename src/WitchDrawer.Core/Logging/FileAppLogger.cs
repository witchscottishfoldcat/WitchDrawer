using System.Text;

namespace WitchDrawer.Core.Logging;

public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logDirectory;
    private readonly object _syncRoot = new();
    private readonly int _retentionDays;

    public FileAppLogger(string logDirectory, int retentionDays = 7)
    {
        _logDirectory = logDirectory;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(_logDirectory);
        TrimOldLogs();
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
        var path = Path.Combine(_logDirectory, $"{DateTimeOffset.Now:yyyy-MM-dd}.log");

        lock (_syncRoot)
        {
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    private void TrimOldLogs()
    {
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Logging must never prevent the app from starting.
        }
    }
}

