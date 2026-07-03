using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Services;

public sealed class UpdateService : IUpdateService
{
    private const string GitHubRepoApiUrl = "https://api.github.com/repos/witchscottishfoldcat/WitchDrawer/releases/latest";
    private const string GitHubReleasePageUrl = "https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest";
    private const string VersionTagPrefix = "v";
    private const string AppExecutableName = "WitchDrawer.App.exe";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler())
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WitchDrawer" }
        }
    };

    // Only these hosts may serve the update payload. Anything else is rejected before download.
    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "codeload.github.com"
    };

    private readonly IAppLogger _logger;

    public UpdateService(IAppLogger logger)
    {
        _logger = logger;
    }

    public event Action<int>? DownloadProgressChanged;

    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion)
    {
        try
        {
            var response = await HttpClient.GetFromJsonAsync<GitHubReleaseResponse>(GitHubRepoApiUrl);

            if (response is null || string.IsNullOrEmpty(response.TagName))
            {
                return new UpdateCheckResult();
            }

            var tagText = response.TagName;
            if (tagText.StartsWith(VersionTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                tagText = tagText[VersionTagPrefix.Length..];
            }

            if (!Version.TryParse(tagText, out var remoteVersion))
            {
                _logger.Info($"Failed to parse remote version tag: {response.TagName}");
                return new UpdateCheckResult();
            }

            var hasUpdate = remoteVersion > currentVersion;
            var downloadUrl = FindAssetUrl(response.Assets);
            var expectedSha = FindSha256ForAssets(response.Body, response.Assets);

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                LatestVersion = remoteVersion,
                ReleaseNotes = TruncateReleaseNotes(response.Body, 500),
                DownloadUrl = downloadUrl ?? (string.IsNullOrEmpty(response.HtmlUrl) ? GitHubReleasePageUrl : response.HtmlUrl),
                ExpectedSha256 = expectedSha
            };
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to check for updates.");
            return new UpdateCheckResult();
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<int>? progress = null, string? expectedSha256 = null)
    {
        if (!IsAllowedDownloadHost(downloadUrl))
        {
            _logger.Error(new InvalidOperationException($"Blocked update download from disallowed host: {downloadUrl}"),
                "Update download rejected.");
            return false;
        }

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WitchDrawerUpdate");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");
            var (downloaded, hash) = await DownloadAsync(downloadUrl, zipPath, progress);
            if (!downloaded)
            {
                _logger.Info("Update download did not complete (empty content length).");
                return false;
            }

            // Hash is computed opportunistically during download. If the release notes published a
            // checksum, a mismatch aborts before any file is extracted or executed.
            if (!string.IsNullOrEmpty(hash))
            {
                _logger.Info($"Downloaded update SHA256: {hash}");
            }

            if (!string.IsNullOrEmpty(expectedSha256)
                && !string.Equals(expectedSha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error(new InvalidOperationException($"Update SHA256 mismatch: expected {expectedSha256}, got {hash}"),
                    "Update integrity check failed; refusing to apply.");
                return false;
            }

            if (!ExtractZipSafely(zipPath, tempDir))
            {
                return false;
            }

            var expectedExe = Path.Combine(tempDir, AppExecutableName);
            if (!File.Exists(expectedExe))
            {
                _logger.Error(new InvalidOperationException("Update package is missing the application executable."),
                    $"Update package did not contain {AppExecutableName}; refusing to apply.");
                return false;
            }

            var appDir = AppContext.BaseDirectory;
            LaunchUpdater(tempDir, appDir);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to download and apply update.");
            return false;
        }
    }

    private async Task<(bool Success, string? Sha256)> DownloadAsync(string downloadUrl, string zipPath, IProgress<int>? progress)
    {
        using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);

        // Hash while streaming so we only read the payload once.
        using var sha = SHA256.Create();
        using var combined = new CryptoStream(fileStream, sha, CryptoStreamMode.Write);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await combined.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;

            if (totalBytes > 0)
            {
                var percent = (int)(bytesRead * 100 / totalBytes);
                progress?.Report(percent);
                DownloadProgressChanged?.Invoke(percent);
            }
        }

        combined.FlushFinalBlock();
        var hash = BitConverter.ToString(sha.Hash ?? []).Replace("-", string.Empty).ToLowerInvariant();

        return bytesRead > 0 ? (true, hash) : (false, hash);
    }

    // Rejects zip entries that would escape the extraction directory (zip-slip) before writing anything.
    private bool ExtractZipSafely(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var destFull = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                // Directory entry; still validate its path.
            }

            var destinationForEntry = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!destinationForEntry.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error(new InvalidOperationException($"Refused zip entry escaping extraction dir: {entry.FullName}"),
                    "Update package contains an unsafe entry; aborting.");
                return false;
            }
        }

        ZipFile.ExtractToDirectory(zipPath, destination, overwriteFiles: true);
        return true;
    }

    // Writes the updater batch with %~1 / %~2 parameters so installer paths are never interpolated
    // into the script body. Quoting the arguments protects against spaces and shell metacharacters.
    private void LaunchUpdater(string tempDir, string appDir)
    {
        var updaterPath = Path.Combine(tempDir, "updater.bat");

        var batContent = """
@echo off
chcp 65001 >nul
setlocal
set "TEMP_DIR=%~1"
set "APP_DIR=%~2"
if "%TEMP_DIR%"=="" exit /b 1
if "%APP_DIR%"=="" exit /b 1

echo Updating WitchDrawer...
timeout /t 2 /nobreak >nul

taskkill /im "WitchDrawer.App.exe" /f >nul 2>&1
timeout /t 1 /nobreak >nul

xcopy "%TEMP_DIR%\*" "%APP_DIR%" /e /y /i >nul 2>&1

start "" "%APP_DIR%WitchDrawer.App.exe"

cd /d "%TEMP%"
rmdir /s /q "%TEMP_DIR%" >nul 2>&1
del "%~f0" >nul 2>&1
endlocal
""";

        File.WriteAllText(updaterPath, batContent);

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = $"\"{tempDir}\" \"{appDir}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
    }

    private static bool IsAllowedDownloadHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return AllowedDownloadHosts.Contains(uri.Host);
    }

    private static string? FindAssetUrl(List<GitHubAsset>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        // Detect architecture to pick the right asset.
        var arch = RuntimeInformation.ProcessArchitecture;
        var archKeyword = arch == Architecture.Arm64 ? "arm64" : "x64";

        var match = assets.FirstOrDefault(a => a.Name.Contains(archKeyword, StringComparison.OrdinalIgnoreCase));
        return (match ?? assets[0]).BrowserDownloadUrl;
    }

    // Parses lines like "sha256:WitchDrawer-x64.zip=abcdef..." from the release body.
    // Returns null when no checksum is published; callers treat that as non-blocking.
    private static string? FindSha256ForAssets(string? body, List<GitHubAsset>? assets)
    {
        if (string.IsNullOrEmpty(body) || assets is null || assets.Count == 0)
        {
            return null;
        }

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = trimmed["sha256:".Length..];
            var eq = payload.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var fileName = payload[..eq].Trim();
            var hash = payload[(eq + 1)..].Trim();
            if (assets.Any(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase)) && hash.Length > 0)
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string TruncateReleaseNotes(string? body, int maxLength)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var clean = body.Replace("\r\n", "\n").Trim();

        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..maxLength] + "...";
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
