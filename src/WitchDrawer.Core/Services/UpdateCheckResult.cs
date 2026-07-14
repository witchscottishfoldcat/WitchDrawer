namespace WitchDrawer.Core.Services;

public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; init; }

    public Version LatestVersion { get; init; } = new(0, 0, 0);

    public string ReleaseNotes { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>
    /// Optional lowercase hex SHA-256 of the download asset when published alongside the release.
    /// </summary>
    public string? ExpectedSha256 { get; init; }
}
