using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Abstractions;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion);
    Task<bool> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        string? expectedSha256 = null);
    Task<bool> ConfirmUpdateStartupAsync();
}
