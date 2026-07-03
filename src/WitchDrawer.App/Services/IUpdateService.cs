namespace WitchDrawer.App.Services;

public interface IUpdateService
{
    event Action<int>? DownloadProgressChanged;

    Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion);

    Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<int>? progress = null, string? expectedSha256 = null);
}
