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

public sealed class MaintenanceViewModel
{
    private readonly AppPaths _appPaths;
    private readonly DataStorageMigrationService _dataStorageMigrationService;
    private readonly DiagnosticLogExportService _diagnosticLogExportService;
    private readonly IAppLogger _logger;
    private readonly UiOperationState _operations;
    private bool IsBusy { get => _operations.IsBusy; set => _operations.IsBusy = value; }
    private string StatusText { set => _operations.StatusText = value; }
    public string CurrentDataDirectory => _appPaths.RootDirectory;

    public MaintenanceViewModel(AppPaths paths, DataStorageMigrationService migration,
        DiagnosticLogExportService diagnostics, IAppLogger logger, UiOperationState operations)
    {
        _appPaths = paths;
        _dataStorageMigrationService = migration;
        _diagnosticLogExportService = diagnostics;
        _logger = logger;
        _operations = operations;
    }

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
                $"v{UpdateViewModel.GetCurrentVersion().ToString(3)}");
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

}
