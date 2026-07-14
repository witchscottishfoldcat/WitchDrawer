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
        try
        {
            if (!IsAllowedDownloadUrl(downloadUrl))
            {
                _logger.Info($"Rejected update download URL: {downloadUrl}");
                return false;
            }

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

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualHash = await ComputeSha256HexAsync(zipPath);
                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info($"Update hash mismatch. expected={expectedSha256} actual={actualHash}");
                    TryDeleteDirectory(tempDir);
                    return false;
                }
            }
            else
            {
                _logger.Info("Update asset has no published SHA-256; continuing with URL allowlist only.");
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
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

            await File.WriteAllTextAsync(updaterPath, batContent, Encoding.ASCII);

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
