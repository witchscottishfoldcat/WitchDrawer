using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class UpdateViewModel : LocalizedObservableObject
{
    protected override void OnLanguageChanged()
    {
        if (!IsCheckingUpdate) UpdateStatusText = string.Empty;
    }

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
            UpdateStatusText = Strings.Get("CheckingForUpdates");

            var currentVersion = GetCurrentVersion();
            var result = await _updateService.CheckForUpdateAsync(currentVersion);

            if (!result.HasUpdate)
            {
                UpdateStatusText = Strings.Format("UpToDateV", currentVersion.Major, currentVersion.Minor, currentVersion.Build);
                StatusText = UpdateStatusText;
                return;
            }

            var versionText = $"v{result.LatestVersion.Major}.{result.LatestVersion.Minor}.{result.LatestVersion.Build}";
            UpdateStatusText = Strings.Format("VersionIsAvailable", versionText);
            StatusText = UpdateStatusText;
            if (_confirmUpdateAsync is not null && await _confirmUpdateAsync(result))
            {
                await ExecuteUpdateAsync(result);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update check failed.");
            UpdateStatusText = Strings.Get("UpdateCheckFailed");
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
            UpdateStatusText = Strings.Get("DownloadingUpdate");

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText = Strings.Format("DownloadingUpdate2", percent);
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(
                result.DownloadUrl,
                progress,
                result.ExpectedSha256);

            if (success)
            {
                _updateStarted = true;
                UpdateStatusText = Strings.Get("UpdateDownloadedRestarting");
                StatusText = UpdateStatusText;
                if (_shutdownAfterUpdateAsync is not null)
                    await _shutdownAfterUpdateAsync();
            }
            else
            {
                UpdateStatusText = Strings.Get("UpdateDownloadFailed");
                StatusText = UpdateStatusText;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update application failed.");
            UpdateStatusText = _updateStarted ? Strings.Get("UpdateReadyQuitWitchDrawerToFinishUpdating") : Strings.Get("UpdateDownloadFailed");
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
