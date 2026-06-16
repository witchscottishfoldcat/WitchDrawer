using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.Core.Services;

public sealed class UpdateService
{
    private const string GitHubRepoApiUrl = "https://api.github.com/repos/witchscottishfoldcat/WitchDrawer/releases/latest";
    private const string GitHubReleasePageUrl = "https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest";
    private const string VersionTagPrefix = "v";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler())
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WitchDrawer" }
        }
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

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                LatestVersion = remoteVersion,
                ReleaseNotes = TruncateReleaseNotes(response.Body, 500),
                DownloadUrl = downloadUrl ?? (string.IsNullOrEmpty(response.HtmlUrl) ? GitHubReleasePageUrl : response.HtmlUrl)
            };
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to check for updates.");
            return new UpdateCheckResult();
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WitchDrawerUpdate");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");
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

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            var appDir = AppContext.BaseDirectory;
            var updaterPath = Path.Combine(tempDir, "updater.bat");

            var batContent = $"""
@echo off
chcp 65001 >nul
echo Updating WitchDrawer...
timeout /t 2 /nobreak >nul

taskkill /im "WitchDrawer.App.exe" /f >nul 2>&1
timeout /t 1 /nobreak >nul

xcopy "{tempDir}\*" "{appDir}" /e /y /i >nul 2>&1

start "" "{appDir}WitchDrawer.App.exe"

cd /d "%temp%"
rmdir /s /q "{tempDir}" >nul 2>&1
del "%~f0" >nul 2>&1
""";

            await File.WriteAllTextAsync(updaterPath, batContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to download and apply update.");
            return false;
        }
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
