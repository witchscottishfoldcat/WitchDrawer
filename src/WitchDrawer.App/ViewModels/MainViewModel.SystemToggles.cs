using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.ViewModels;

// 系统开关、更新检查、数据迁移、诊断日志与自动隐藏、图标提示设置。
public sealed partial class MainViewModel
{
    /// <summary>
    /// 将数据目录整体迁移到新文件夹。成功后需重启应用才会切换到新目录。
    /// </summary>
    public async Task MigrateDataDirectoryAsync(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariableName)))
        {
            throw new InvalidOperationException(
                $"当前数据目录由 {AppPaths.DataDirectoryEnvironmentVariableName} 指定。请先移除该环境变量并重启，再迁移数据目录。");
        }
        IsBusy = true;
        StatusText = "正在迁移数据目录…";
        try
        {
            var newPaths = await _dataStorageMigrationService.MigrateAsync(targetDirectory);
            StatusText = "数据已迁移，重启后生效";
            _logger.Info($"Data directory migrated to {newPaths.RootDirectory}. Restart required.");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Data directory migration failed.");
            StatusText = "数据目录迁移失败";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<DiagnosticLogExportResult> ExportDiagnosticLogsAsync(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (IsBusy)
        {
            LogInfoWithoutThrowing("Diagnostic log export skipped because another main operation is running.");
            throw new InvalidOperationException("正在处理其他操作，请稍后再导出诊断日志。");
        }

        IsBusy = true;
        StatusText = "正在导出诊断日志…";
        LogInfoWithoutThrowing("Diagnostic log export started.");
        try
        {
            var result = await _diagnosticLogExportService.ExportAsync(
                destinationPath,
                CurrentVersionText);
            StatusText = $"已导出 {result.LogFileCount} 个日志文件";
            LogInfoWithoutThrowing($"Diagnostic log export completed with {result.LogFileCount} log file(s).");
            return result;
        }
        catch (Exception exception)
        {
            LogErrorWithoutThrowing(exception, "Diagnostic log export failed.");
            StatusText = "诊断日志导出失败";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LogInfoWithoutThrowing(string message)
    {
        try
        {
            _logger.Info(message);
        }
        catch
        {
            // Diagnostic export must remain usable when the normal log destination is unavailable.
        }
    }

    private void LogErrorWithoutThrowing(Exception exception, string message)
    {
        try
        {
            _logger.Error(exception, message);
        }
        catch
        {
            // The original export failure is more useful than a secondary logging failure.
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Operation failed.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            FlushPendingDesktopReload();
        }
    }

    private async Task ToggleLaunchOnStartupAsync()
    {
        try
        {
            var newState = !LaunchOnStartup;
            WriteStartupRegistry(newState);
            LaunchOnStartup = newState;
            StatusText = newState ? "已开启开机自启动" : "已关闭开机自启动";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle startup registry key.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopIconsAsync()
    {
        try
        {
            var hidden = !AreDesktopIconsHidden;
            await DesktopIconVisibility.SetHiddenAsync(hidden);
            AreDesktopIconsHidden = hidden;
            StatusText = hidden ? "已隐藏 Windows 桌面图标" : "已显示 Windows 桌面图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle Windows desktop icons.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopDoubleClickAsync()
    {
        try
        {
            var enabled = !IsDesktopDoubleClickEnabled;
            await _drawerService.SetSettingAsync(
                DesktopDoubleClickSettingKey,
                enabled.ToString());
            IsDesktopDoubleClickEnabled = enabled;
            StatusText = enabled ? "已开启桌面双击切换图标" : "已关闭桌面双击切换图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop double-click setting.");
            OnPropertyChanged(nameof(IsDesktopDoubleClickEnabled));
            StatusText = exception.Message;
        }
    }

    private static bool ReadStartupRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            var value = key?.GetValue(StartupRegistryKeyName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteStartupRegistry(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null)
        {
            return;
        }

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\" --silent");
            }
        }
        else
        {
            key.DeleteValue(StartupRegistryKeyName, throwOnMissingValue: false);
        }
    }

    private async Task ToggleIconToolTipCompactAsync()
    {
        try
        {
            var compact = !IconToolTipCompact;
            await _drawerService.SetSettingAsync(
                IconToolTipCompactSettingKey,
                compact.ToString());
            IconToolTipCompact = compact;
            PublishIconToolTipMode();
            StatusText = compact
                ? "图标名称已设为精简显示"
                : "图标名称已设为完整显示";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save icon tooltip compact setting.");
            OnPropertyChanged(nameof(IconToolTipCompact));
            StatusText = exception.Message;
        }
    }

    private void PublishIconToolTipMode()
    {
        WeakReferenceMessenger.Default.Send(
            new IconToolTipModeChangedMessage(IconToolTipCompact));
    }

    private async Task ToggleAutoHideEnabledAsync()
    {
        try
        {
            var enabled = !AutoHideEnabled;
            AutoHideEnabled = enabled;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = enabled ? "已开启自动隐藏" : "已关闭自动隐藏";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide enable setting.");
            OnPropertyChanged(nameof(AutoHideEnabled));
            StatusText = exception.Message;
        }
    }

    private async Task ApplyAutoHideRevealScopeAsync(AutoHideRevealScope scope)
    {
        try
        {
            AutoHideRevealScope = scope;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = scope == AutoHideRevealScope.AllBoxes
                ? "悬停任一收纳盒将全部显示"
                : "悬停某个收纳盒仅其内容显示";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide reveal scope setting.");
            OnPropertyChanged(nameof(AutoHideRevealScope));
            StatusText = exception.Message;
        }
    }

    private Task SaveAutoHideSettingsAsync()
    {
        return _autoHideSettingsStore.SaveAsync(new AutoHideSettings(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void PublishAutoHideSettings()
    {
        WeakReferenceMessenger.Default.Send(new AutoHideSettingsChangedMessage(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void QueueAutoHideSave()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _autoHideSaveCts, next);
        // 只取消不立即 Dispose：旧任务可能仍挂在该 token 的 Task.Delay 上，
        // 此时 Dispose 会让其回调注册抛出 ObjectDisposedException（被误记为保存失败）。
        // 已取消且无注册的 CancellationTokenSource 由 GC 回收即可。
        previous?.Cancel();

        _ = PersistAutoHideAfterDelayAsync(next.Token);
    }

    private async Task PersistAutoHideAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 应用已在属性 setter 中即时完成，这里只负责持久化。
            await SaveAutoHideSettingsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide settings.");
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

    private static Version GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }
}
