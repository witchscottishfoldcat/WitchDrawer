using WitchDrawer.Core.Localization;
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
                Strings.Format("TheCurrentDataFolderIsSetByRemoveThis", AppPaths.DataDirectoryEnvironmentVariableName));
        }
        IsBusy = true;
        StatusText = Strings.Get("MigratingDataFolder");
        try
        {
            var newPaths = await _dataStorageMigrationService.MigrateAsync(targetDirectory);
            StatusText = Strings.Get("DataMigratedRestartToApply");
            _logger.Info($"Data directory migrated to {newPaths.RootDirectory}. Restart required.");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Data directory migration failed.");
            StatusText = Strings.Get("DataFolderMigrationFailed");
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
            throw new InvalidOperationException(Strings.Get("AnotherOperationIsInProgressExportDiagnosticLogsLater"));
        }

        IsBusy = true;
        StatusText = Strings.Get("ExportingDiagnosticLogs");
        LogInfoWithoutThrowing("Diagnostic log export started.");
        try
        {
            if (_logger is FileAppLogger fileLogger) await fileLogger.FlushAsync();
            var result = await _diagnosticLogExportService.ExportAsync(
                destinationPath,
                $"v{UpdateViewModel.GetCurrentVersion().ToString(3)}");
            StatusText = Strings.Format("ExportedLogFiles", result.LogFileCount);
            LogInfoWithoutThrowing($"Diagnostic log export completed with {result.LogFileCount} log file(s).");
            return result;
        }
        catch (Exception exception)
        {
            LogErrorWithoutThrowing(exception, "Diagnostic log export failed.");
            StatusText = Strings.Get("DiagnosticLogExportFailed");
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
