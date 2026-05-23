namespace WitchDrawer.Core.Services;

public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; init; }

    public Version LatestVersion { get; init; } = new(0, 0, 0);

    public string ReleaseNotes { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;
}
