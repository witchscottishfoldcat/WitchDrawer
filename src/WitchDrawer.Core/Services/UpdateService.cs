using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.Core.Services;

public sealed class UpdateService
{
    private const string GitHubOwner = "witchscottishfoldcat";
    private const string GitHubRepo = "WitchDrawer";
    private const string GitHubRepoApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string GitHubReleasePageUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string VersionTagPrefix = "v";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler())
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WitchDrawer" }
        }
    };

    private static readonly Regex Sha256HexRegex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    private readonly IAppLogger _logger;

    public UpdateService(IAppLogger logger)
    {
        _logger = logger;
    }

    public event Action<int>? DownloadProgressChanged;

    public async Task CleanupLegacyUpdaterArtifactsAsync()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var removedCount = await Task.Run(() => CleanupLegacyUpdaterArtifacts(appDirectory));
        if (removedCount > 0)
        {
            _logger.Info($"Removed {removedCount} legacy updater artifact(s).");
        }
    }

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
            var (downloadUrl, expectedSha256) = await ResolveAssetAsync(response.Assets);

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl = string.IsNullOrEmpty(response.HtmlUrl) ? GitHubReleasePageUrl : response.HtmlUrl;
            }

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                LatestVersion = remoteVersion,
                ReleaseNotes = TruncateReleaseNotes(response.Body, 500),
                DownloadUrl = downloadUrl,
                ExpectedSha256 = expectedSha256
            };
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to check for updates.");
            return new UpdateCheckResult();
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        string? expectedSha256 = null)
    {
        string? tempRoot = null;
        string? updaterPath = null;

        try
        {
            if (!IsAllowedDownloadUrl(downloadUrl))
            {
                _logger.Info($"Rejected update download URL: {downloadUrl}");
                return false;
            }

            var updateId = Guid.NewGuid().ToString("N");
            tempRoot = Path.Combine(
                Path.GetTempPath(),
                "WitchDrawerUpdate",
                updateId);
            var payloadDir = Path.Combine(tempRoot, "payload");
            Directory.CreateDirectory(payloadDir);

            var zipPath = Path.Combine(tempRoot, "update.zip");
            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);

                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        var percent = (int)(bytesRead * 100 / totalBytes);
                        progress?.Report(percent);
                        DownloadProgressChanged?.Invoke(percent);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualHash = await ComputeSha256HexAsync(zipPath);
                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info($"Update hash mismatch. expected={expectedSha256} actual={actualHash}");
                    TryDeleteDirectory(tempRoot);
                    return false;
                }
            }
            else
            {
                _logger.Info("Update asset has no published SHA-256; continuing with URL allowlist only.");
            }

            await Task.Run(() =>
                System.IO.Compression.ZipFile.ExtractToDirectory(
                    zipPath,
                    payloadDir,
                    overwriteFiles: true));

            var appExecutablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appExecutablePath))
            {
                _logger.Info("Cannot apply update because the current executable path is unavailable.");
                TryDeleteDirectory(tempRoot);
                return false;
            }

            appExecutablePath = Path.GetFullPath(appExecutablePath);
            var appDirectory = Path.GetDirectoryName(appExecutablePath);
            var executableName = Path.GetFileName(appExecutablePath);
            if (string.IsNullOrWhiteSpace(appDirectory)
                || string.IsNullOrWhiteSpace(executableName)
                || !Directory.Exists(appDirectory))
            {
                _logger.Info($"Cannot apply update because the application directory is invalid: {appDirectory}");
                TryDeleteDirectory(tempRoot);
                return false;
            }

            var payloadExecutablePath = Path.Combine(payloadDir, executableName);
            if (!File.Exists(payloadExecutablePath))
            {
                _logger.Info($"Downloaded update does not contain {executableName}.");
                TryDeleteDirectory(tempRoot);
                return false;
            }

            // Keep the running batch outside the directory it removes. This avoids
            // cmd.exe losing its current script/path while cleaning the update payload.
            updaterPath = Path.Combine(Path.GetTempPath(), $"WitchDrawerUpdater-{updateId}.bat");
            var updateLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WitchDrawer",
                "Logs",
                "updater.log");
            Directory.CreateDirectory(Path.GetDirectoryName(updateLogPath)!);
            await File.WriteAllTextAsync(updaterPath, BuildUpdaterScript(), Encoding.ASCII);

            var updaterProcess = Process.Start(CreateUpdaterStartInfo(
                updaterPath,
                tempRoot,
                payloadDir,
                appDirectory,
                appExecutablePath,
                executableName,
                updateLogPath));
            if (updaterProcess is null)
            {
                _logger.Info("Failed to start the update helper process.");
                TryDeleteDirectory(tempRoot);
                TryDeleteFile(updaterPath);
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(tempRoot))
            {
                TryDeleteDirectory(tempRoot);
            }
            if (!string.IsNullOrWhiteSpace(updaterPath))
            {
                TryDeleteFile(updaterPath);
            }

            _logger.Error(exception, "Failed to download and apply update.");
            return false;
        }
    }

    internal static string BuildUpdaterScript()
    {
        return """"
@echo off
setlocal
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update started.
timeout /t 2 /nobreak >nul

taskkill /im "%WITCHDRAWER_EXE_NAME%" /f >nul 2>&1
timeout /t 1 /nobreak >nul

xcopy "%WITCHDRAWER_PAYLOAD%\*" "%WITCHDRAWER_APP_DIR%" /e /y /i >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 1 goto update_failed

del /q "%WITCHDRAWER_APP_DIR%\update.zip" "%WITCHDRAWER_APP_DIR%\updater.bat" >nul 2>&1

start "" /b /d "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_APP_EXE%" >nul 2>&1
if errorlevel 1 goto update_failed

>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update completed.
cd /d "%TEMP%"
rmdir /s /q "%WITCHDRAWER_UPDATE_ROOT%" >nul 2>&1
start "" /b "%ComSpec%" /d /c del /q "%~f0" >nul 2>&1 & exit /b 0

:update_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update failed with exit code %errorlevel%.
exit /b 1
"""";
    }

    internal static ProcessStartInfo CreateUpdaterStartInfo(
        string updaterPath,
        string tempRoot,
        string payloadDirectory,
        string appDirectory,
        string appExecutablePath,
        string executableName,
        string updateLogPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /s /c \"\"{updaterPath}\"\"",
            WorkingDirectory = Path.GetDirectoryName(updaterPath)
                ?? Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.Environment["WITCHDRAWER_UPDATE_ROOT"] = tempRoot;
        startInfo.Environment["WITCHDRAWER_PAYLOAD"] = payloadDirectory;
        startInfo.Environment["WITCHDRAWER_APP_DIR"] = appDirectory;
        startInfo.Environment["WITCHDRAWER_APP_EXE"] = appExecutablePath;
        startInfo.Environment["WITCHDRAWER_EXE_NAME"] = executableName;
        startInfo.Environment["WITCHDRAWER_UPDATE_LOG"] = updateLogPath;
        return startInfo;
    }

    internal static int CleanupLegacyUpdaterArtifacts(string appDirectory)
    {
        var removedCount = 0;
        foreach (var fileName in new[] { "update.zip", "updater.bat" })
        {
            if (TryDeleteFile(Path.Combine(appDirectory, fileName)))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    internal static bool IsAllowedDownloadUrl(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Contains($"/{GitHubOwner}/{GitHubRepo}/", StringComparison.OrdinalIgnoreCase);
        }

        if (host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task<(string? DownloadUrl, string? Sha256)> ResolveAssetAsync(List<GitHubAsset>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return (null, null);
        }

        var arch = RuntimeInformation.ProcessArchitecture;
        var archKeyword = arch == Architecture.Arm64 ? "arm64" : "x64";

        var zipAssets = assets
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var match = zipAssets.FirstOrDefault(asset => asset.Name.Contains(archKeyword, StringComparison.OrdinalIgnoreCase))
            ?? zipAssets.FirstOrDefault()
            ?? assets.FirstOrDefault(asset => asset.Name.Contains(archKeyword, StringComparison.OrdinalIgnoreCase))
            ?? assets[0];

        if (match is null || string.IsNullOrWhiteSpace(match.BrowserDownloadUrl))
        {
            return (null, null);
        }

        if (!IsAllowedDownloadUrl(match.BrowserDownloadUrl))
        {
            _logger.Info($"Rejected release asset URL: {match.BrowserDownloadUrl}");
            return (null, null);
        }

        var sha256 = await TryResolveSha256Async(assets, match);
        return (match.BrowserDownloadUrl, sha256);
    }

    private async Task<string?> TryResolveSha256Async(List<GitHubAsset> assets, GitHubAsset packageAsset)
    {
        var companion = assets.FirstOrDefault(asset =>
            asset.Name.Equals(packageAsset.Name + ".sha256", StringComparison.OrdinalIgnoreCase)
            || asset.Name.Equals(packageAsset.Name + ".sha256.txt", StringComparison.OrdinalIgnoreCase));

        if (companion is not null && IsAllowedDownloadUrl(companion.BrowserDownloadUrl))
        {
            return await ReadSha256FromAssetAsync(companion.BrowserDownloadUrl, packageAsset.Name);
        }

        var checksums = assets.FirstOrDefault(asset =>
            asset.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)
            || asset.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase)
            || asset.Name.EndsWith(".sha256sums", StringComparison.OrdinalIgnoreCase));

        if (checksums is not null && IsAllowedDownloadUrl(checksums.BrowserDownloadUrl))
        {
            return await ReadSha256FromAssetAsync(checksums.BrowserDownloadUrl, packageAsset.Name);
        }

        return null;
    }

    private async Task<string?> ReadSha256FromAssetAsync(string url, string packageFileName)
    {
        try
        {
            var text = await HttpClient.GetStringAsync(url);
            foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var candidateHash = parts[0].Trim().TrimStart('*');
                if (!Sha256HexRegex.IsMatch(candidateHash))
                {
                    continue;
                }

                if (parts.Length == 1)
                {
                    return candidateHash.ToLowerInvariant();
                }

                var fileName = parts[^1].Trim().TrimStart('*');
                if (string.Equals(Path.GetFileName(fileName), packageFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, packageFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateHash.ToLowerInvariant();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to read update checksum asset.");
        }

        return null;
    }

    private static async Task<string> ComputeSha256HexAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch
        {
        }

        return false;
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
