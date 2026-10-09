using WitchDrawer.Core.Logging;

namespace WitchDrawer.Core.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public async Task FlushAndDispose_PreserveQueuedMessagesAndExceptionDetails()
    {
        var root = CreateRoot();
        try
        {
            var logger = new FileAppLogger(root);
            for (var i = 0; i < 200; i++) logger.Info($"entry {i}");
            logger.Error(new IOException("test failure"), "original operation failed");
            await logger.FlushAsync();
            var logPath = Assert.Single(Directory.GetFiles(root, "*.log"));
            var text = await File.ReadAllTextAsync(logPath);
            Assert.Contains("entry 0", text);
            Assert.Contains("entry 199", text);
            Assert.Contains("System.IO.IOException: test failure", text);
            logger.Info("shutdown entry");
            await logger.DisposeAsync();
            await logger.DisposeAsync();
            Assert.Contains("shutdown entry", await File.ReadAllTextAsync(logPath));
            logger.Info("after disposal"); // Logging remains safe during late shutdown callbacks.
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnavailableLogDirectory_DoesNotThrowAndRecoversWhenItBecomesWritable()
    {
        var root = CreateRoot();
        var destination = Path.Combine(root, "logs");
        await File.WriteAllTextAsync(destination, "a file blocks directory creation");
        try
        {
            await using var logger = new FileAppLogger(destination);
            logger.Info("cannot write");
            logger.Error(new IOException("original failure"), "error path");
            await logger.FlushAsync();
            Assert.Equal("a file blocks directory creation", await File.ReadAllTextAsync(destination));
            File.Delete(destination);
            logger.Info("recovered");
            await logger.FlushAsync();
            Assert.Contains("recovered", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(destination))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LockedLogFile_DoesNotThrowIntoProducerOrFlush()
    {
        var root = CreateRoot();
        var path = Path.Combine(root, DateTimeOffset.Now.ToString("yyyy-MM-dd") + ".log");
        try
        {
            await using var logger = new FileAppLogger(root);
            using (var locked = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (var i = 0; i < 5000; i++) logger.Info("burst entry");
                logger.Error(new IOException("original error"), "must not escape");
                await logger.FlushAsync();
            }
            logger.Info("after unlock");
            await logger.FlushAsync();
            Assert.Contains("after unlock", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.LoggerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
