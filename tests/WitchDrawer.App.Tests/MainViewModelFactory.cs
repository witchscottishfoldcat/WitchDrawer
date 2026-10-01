using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

internal static class MainViewModelFactory
{
    internal static MainViewModel Create(DrawerService drawer, TodoService todo,
        IFileLauncher launcher, IShellChangeNotifier notifier, IAppLogger logger,
        QuickPanelViewModel quickPanel, UpdateService update, BoxVisualStyleStore styles,
        BoxPositionLockStateStore locks, AppPaths paths, DataStorageMigrationService migration,
        AutoHideSettingsStore autoHide)
    {
        var state = new UiOperationState(logger);
        return new MainViewModel(drawer, todo, launcher, notifier, logger, styles, locks,
            new SettingsViewModel(drawer.Settings, logger, new WindowsDesktopIntegration(), autoHide, state),
            new UpdateViewModel(update, logger, state), new ArchiveViewModel(drawer, todo, logger, state),
            new MaintenanceViewModel(paths, migration, new DiagnosticLogExportService(paths), logger, state), state);
    }
}
