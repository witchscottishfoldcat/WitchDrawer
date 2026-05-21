using System.Windows;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        AppThemeManager.Apply(AppTheme.Moe);

        try
        {
            var paths = AppPaths.ForCurrentUser();
            paths.EnsureCreated();

            var logger = new FileAppLogger(paths.LogsDirectory);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            var launcher = new ShellFileLauncher();
            var trash = new FileSystemRecycleBin();

            await drawerService.InitializeAsync();

            var quickPanelViewModel = new QuickPanelViewModel(drawerService, launcher, logger);
            var quickPanel = new QuickPanelWindow(quickPanelViewModel);
            var mainViewModel = new MainViewModel(drawerService, launcher, trash, logger, quickPanelViewModel);
            var desktopBoxManager = new DesktopBoxManager(drawerService, launcher, trash, logger);
            var mainWindow = new MainWindow(mainViewModel, quickPanel, logger);

            mainViewModel.BoxesChanged += async (_, _) => await desktopBoxManager.RefreshAsync();
            mainViewModel.ItemsChanged += async (_, _) =>
            {
                await quickPanelViewModel.LoadAsync();
                await desktopBoxManager.RefreshAsync();
            };
            desktopBoxManager.ItemsChanged += async (_, _) =>
            {
                await mainViewModel.LoadAsync();
                await quickPanelViewModel.LoadAsync();
            };
            mainWindow.Closed += (_, _) => desktopBoxManager.CloseAll();

            MainWindow = mainWindow;
            mainWindow.Show();
            await mainViewModel.LoadAsync();
            await quickPanelViewModel.LoadAsync();
            await desktopBoxManager.RefreshAsync();
        }
        catch (Exception exception)
        {
            var sb = new System.Text.StringBuilder();
            var ex = exception;
            while (ex != null)
            {
                sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
                sb.AppendLine(ex.StackTrace);
                sb.AppendLine("---");
                ex = ex.InnerException;
            }
            MessageBox.Show(
                sb.ToString(),
                "WitchDrawer startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
