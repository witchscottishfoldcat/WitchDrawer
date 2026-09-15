using System.IO.Compression;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class DiagnosticLogExportServiceTests
{
    [Fact]
    public async Task ExportAsync_CreatesPrivacyScopedArchive()
    {
        using var workspace = new TestWorkspace();
        var logPath = Path.Combine(workspace.Paths.LogsDirectory, "2026-09-15.log");
        await File.WriteAllTextAsync(logPath, "diagnostic event");
        await File.WriteAllTextAsync(workspace.Paths.DatabasePath, "private database content");
        var destinationPath = Path.Combine(workspace.OutputDirectory, "diagnostics.zip");

        var result = await workspace.Service.ExportAsync(destinationPath, "v1.3.13");

        Assert.Equal(Path.GetFullPath(destinationPath), result.ArchivePath);
        Assert.Equal(1, result.LogFileCount);
        using var archive = ZipFile.OpenRead(destinationPath);
        Assert.NotNull(archive.GetEntry("logs/2026-09-15.log"));
        Assert.NotNull(archive.GetEntry("diagnostic-info.txt"));
        Assert.NotNull(archive.GetEntry("README.txt"));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("witchdrawer.db"));
        Assert.Equal("diagnostic event", await ReadEntryAsync(archive, "logs/2026-09-15.log"));
        Assert.Contains("ApplicationVersion: v1.3.13", await ReadEntryAsync(archive, "diagnostic-info.txt"));
        Assert.Contains("does not contain the WitchDrawer database", await ReadEntryAsync(archive, "README.txt"));
    }

    [Fact]
    public async Task ExportAsync_WhenNoLogsExist_CreatesUsefulArchive()
    {
        using var workspace = new TestWorkspace();
        var destinationPath = Path.Combine(workspace.OutputDirectory, "diagnostics.zip");

        var result = await workspace.Service.ExportAsync(destinationPath, "v1.3.13");

        Assert.Equal(0, result.LogFileCount);
        using var archive = ZipFile.OpenRead(destinationPath);
        Assert.NotNull(archive.GetEntry("diagnostic-info.txt"));
        Assert.NotNull(archive.GetEntry("README.txt"));
    }

    [Fact]
    public async Task ExportAsync_WhenDestinationExists_ReplacesIt()
    {
        using var workspace = new TestWorkspace();
        var destinationPath = Path.Combine(workspace.OutputDirectory, "diagnostics.zip");
        await File.WriteAllTextAsync(destinationPath, "old content");

        await workspace.Service.ExportAsync(destinationPath, "v1.3.13");

        using var archive = ZipFile.OpenRead(destinationPath);
        Assert.NotNull(archive.GetEntry("diagnostic-info.txt"));
    }

    [Fact]
    public async Task ExportAsync_WhileCurrentLogIsOpen_IncludesReadableSnapshot()
    {
        using var workspace = new TestWorkspace();
        var logPath = Path.Combine(workspace.Paths.LogsDirectory, "current.log");
        await using var activeLog = new FileStream(
            logPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        await using (var writer = new StreamWriter(activeLog, leaveOpen: true))
        {
            await writer.WriteAsync("active diagnostic event");
            await writer.FlushAsync();
        }

        var destinationPath = Path.Combine(workspace.OutputDirectory, "diagnostics.zip");
        await workspace.Service.ExportAsync(destinationPath, "v1.3.13");

        using var archive = ZipFile.OpenRead(destinationPath);
        Assert.Equal("active diagnostic event", await ReadEntryAsync(archive, "logs/current.log"));
    }

    [Fact]
    public async Task ExportAsync_WhenDestinationIsNotZip_RejectsWithoutOverwritingFile()
    {
        using var workspace = new TestWorkspace();
        var destinationPath = Path.Combine(workspace.OutputDirectory, "keep.log");
        await File.WriteAllTextAsync(destinationPath, "keep this content");

        await Assert.ThrowsAsync<ArgumentException>(
            () => workspace.Service.ExportAsync(destinationPath, "v1.3.13"));

        Assert.Equal("keep this content", await File.ReadAllTextAsync(destinationPath));
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing {entryName}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "WitchDrawer.DiagnosticExportTests", Guid.NewGuid().ToString("N"));
            Paths = new AppPaths(Path.Combine(Root, "data"));
            Paths.EnsureCreated();
            OutputDirectory = Path.Combine(Root, "output");
            Directory.CreateDirectory(OutputDirectory);
            Service = new DiagnosticLogExportService(Paths);
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public string OutputDirectory { get; }

        public DiagnosticLogExportService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
