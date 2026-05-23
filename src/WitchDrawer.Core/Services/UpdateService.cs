using System.Net.Http.Json;
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

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                LatestVersion = remoteVersion,
                ReleaseNotes = TruncateReleaseNotes(response.Body, 500),
                DownloadUrl = string.IsNullOrEmpty(response.HtmlUrl) ? GitHubReleasePageUrl : response.HtmlUrl
            };
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to check for updates.");
            return new UpdateCheckResult();
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
    }
}
