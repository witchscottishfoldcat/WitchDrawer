using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Core.Services;

public sealed class DiagnosticLogExportService(AppPaths paths)
{
    private const string DiagnosticInfoEntryName = "diagnostic-info.txt";
    private const string ReadmeEntryName = "README.txt";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public Task<DiagnosticLogExportResult> ExportAsync(
        string destinationPath,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Diagnostic archive path cannot be empty.", nameof(destinationPath));
        }

        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            throw new ArgumentException("Application version cannot be empty.", nameof(applicationVersion));
        }

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(fullDestinationPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Diagnostic archive path must use the .zip extension.", nameof(destinationPath));
        }

        return Task.Run(
            () => ExportCore(fullDestinationPath, applicationVersion.Trim(), cancellationToken),
            cancellationToken);
    }

    private DiagnosticLogExportResult ExportCore(
        string destinationPath,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Diagnostic archive directory is unavailable.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var logFiles = GetLogFiles();
            int includedLogCount;
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                includedLogCount = AddLogs(archive, logFiles, cancellationToken);
                AddTextEntry(
                    archive,
                    DiagnosticInfoEntryName,
                    BuildDiagnosticInfo(applicationVersion, includedLogCount));
                AddTextEntry(archive, ReadmeEntryName, BuildReadme());
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            return new DiagnosticLogExportResult(destinationPath, includedLogCount);
        }
        finally
        {
            TryDeleteTemporaryArchive(temporaryPath);
        }
    }

    private string[] GetLogFiles()
    {
        if (!Directory.Exists(paths.LogsDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int AddLogs(
        ZipArchive archive,
        IEnumerable<string> logFiles,
        CancellationToken cancellationToken)
    {
        var includedCount = 0;
        foreach (var logFile in logFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var source = new FileStream(
                    logFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var entry = archive.CreateEntry(
                    $"logs/{Path.GetFileName(logFile)}",
                    CompressionLevel.Optimal);
                using var destination = entry.Open();
                source.CopyTo(destination);
                includedCount++;
            }
            catch (FileNotFoundException)
            {
                // Retention cleanup can remove an old log between enumeration and copying.
            }
            catch (DirectoryNotFoundException)
            {
                // The configured data location may become temporarily unavailable mid-export.
            }
        }

        return includedCount;
    }

    private string BuildDiagnosticInfo(string applicationVersion, int includedLogCount)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"CollectedAtUtc: {DateTimeOffset.UtcNow:O}",
                $"ApplicationVersion: {applicationVersion}",
                $"WindowsVersion: {Environment.OSVersion}",
                $"OSDescription: {RuntimeInformation.OSDescription}",
                $"OSArchitecture: {RuntimeInformation.OSArchitecture}",
                $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}",
                $"Framework: {RuntimeInformation.FrameworkDescription}",
                $"DataDirectory: {paths.RootDirectory}",
                $"DataDirectoryAvailable: {Directory.Exists(paths.RootDirectory)}",
                $"LogsDirectoryAvailable: {Directory.Exists(paths.LogsDirectory)}",
                $"LogFilesIncluded: {includedLogCount}"
            ]);
    }

    private static string BuildReadme()
    {
        return string.Join(
            Environment.NewLine,
            [
                "WitchDrawer diagnostic log export",
                string.Empty,
                "This archive contains application logs and basic runtime information.",
                "It does not contain the WitchDrawer database or user file contents.",
                "Logs and diagnostic information may contain file names and full paths.",
                "Please review the archive before sending it to support."
            ]);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8WithoutBom, leaveOpen: false);
        writer.Write(content);
    }

    private static void TryDeleteTemporaryArchive(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed cleanup must not hide the original export result or exception.
        }
    }
}
