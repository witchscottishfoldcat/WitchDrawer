using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private readonly IAppLogger _logger;
    private readonly UiOperationState _operations;
    private string StatusText { set => _operations.StatusText = value; }
    private string _updateStatusText = string.Empty;
    private bool _isCheckingUpdate;
    private string? _pendingUpdateSha256;

    public UpdateViewModel(UpdateService updateService, IAppLogger logger, UiOperationState operations)
    {
        _updateService = updateService;
        _logger = logger;
        _operations = operations;
        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
    }

    public IAsyncRelayCommand CheckForUpdateCommand { get; }
    public event EventHandler<UpdateCheckResult>? UpdateRequested;
    public event EventHandler? UpdateConfirmed;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set => SetProperty(ref _isCheckingUpdate, value);
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
        if (IsCheckingUpdate)
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
            _pendingUpdateSha256 = result.ExpectedSha256;

            UpdateRequested?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update check failed.");
            UpdateStatusText = "检查更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public async Task ExecuteUpdateAsync(string downloadUrl)
    {
        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在下载更新...";

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText = $"正在下载更新... {percent}%";
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(
                downloadUrl,
                progress,
                _pendingUpdateSha256);

            if (success)
            {
                UpdateStatusText = "更新下载完成，正在重启...";
                StatusText = UpdateStatusText;
                UpdateConfirmed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UpdateStatusText = "下载更新失败";
                StatusText = UpdateStatusText;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update download failed.");
            UpdateStatusText = "下载更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    internal static Version GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }
}
