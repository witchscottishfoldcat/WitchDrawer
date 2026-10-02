using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private readonly IAppLogger _logger;
    private readonly UiOperationState _operations;
    private readonly Func<UpdateCheckResult, Task<bool>>? _confirmUpdateAsync;
    private readonly Func<Task>? _shutdownAfterUpdateAsync;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private string StatusText { set => _operations.StatusText = value; }
    private string _updateStatusText = string.Empty;
    private bool _isCheckingUpdate;
    private bool _updateStarted;

    public UpdateViewModel(UpdateService updateService, IAppLogger logger, UiOperationState operations,
        Func<UpdateCheckResult, Task<bool>>? confirmUpdateAsync = null,
        Func<Task>? shutdownAfterUpdateAsync = null)
    {
        _updateService = updateService;
        _logger = logger;
        _operations = operations;
        _confirmUpdateAsync = confirmUpdateAsync;
        _shutdownAfterUpdateAsync = shutdownAfterUpdateAsync;
        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync, () => !IsCheckingUpdate);
    }

    public IAsyncRelayCommand CheckForUpdateCommand { get; }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingUpdate, value))
                CheckForUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public string CurrentVersionText
    {
        get
        {
            var version = GetCurrentVersion();
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private async Task CheckForUpdateAsync()
    {
        // The command owns confirmation, download and shutdown as one operation.
        // Also reject direct ExecuteAsync calls, which can bypass CanExecute.
        if (_updateStarted || !await _updateGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在检查更新...";

            var currentVersion = GetCurrentVersion();
            var result = await _updateService.CheckForUpdateAsync(currentVersion);

            if (!result.HasUpdate)
            {
                UpdateStatusText = $"已是最新版本 v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
                StatusText = UpdateStatusText;
                return;
            }

            var versionText = $"v{result.LatestVersion.Major}.{result.LatestVersion.Minor}.{result.LatestVersion.Build}";
            UpdateStatusText = $"发现新版本 {versionText}";
            StatusText = UpdateStatusText;
            if (_confirmUpdateAsync is not null && await _confirmUpdateAsync(result))
            {
                await ExecuteUpdateAsync(result);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update check failed.");
            UpdateStatusText = "检查更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            // Once an installer has started, it is waiting for this process to exit.
            // Never allow another installer even if shutdown is delayed or fails.
            if (!_updateStarted) IsCheckingUpdate = false;
            _updateGate.Release();
        }
    }

    private async Task ExecuteUpdateAsync(UpdateCheckResult result)
    {
        try
        {
            UpdateStatusText = "正在下载更新...";

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText = $"正在下载更新... {percent}%";
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(
                result.DownloadUrl,
                progress,
                result.ExpectedSha256);

            if (success)
            {
                _updateStarted = true;
                UpdateStatusText = "更新下载完成，正在重启...";
                StatusText = UpdateStatusText;
                if (_shutdownAfterUpdateAsync is not null)
                    await _shutdownAfterUpdateAsync();
            }
            else
            {
                UpdateStatusText = "下载更新失败";
                StatusText = UpdateStatusText;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update application failed.");
            UpdateStatusText = _updateStarted ? "更新已准备就绪，请退出 WitchDrawer 以完成更新" : "下载更新失败";
            StatusText = UpdateStatusText;
        }
    }

    internal static Version GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }
}
