using System.Windows;
using System.Windows.Interop;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;
using WitchDrawer.Native.Files;
using WitchDrawer.Native.Shell;

namespace WitchDrawer.App;

public partial class App : Application
{
    private TaskbarIcon? _taskbarIcon;
    private MainWindow? _mainWindow;
    private DesktopBoxManager? _desktopBoxManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
            var desktopBoxLayoutSettings = new DesktopBoxLayoutSettings();

            await drawerService.InitializeAsync();

            var quickPanelViewModel = new QuickPanelViewModel(drawerService, launcher, logger);
            var quickPanel = new QuickPanelWindow(quickPanelViewModel);
            var mainViewModel = new MainViewModel(drawerService, launcher, trash, logger, quickPanelViewModel, desktopBoxLayoutSettings);
            _desktopBoxManager = new DesktopBoxManager(drawerService, launcher, trash, logger, desktopBoxLayoutSettings);
            _mainWindow = new MainWindow(mainViewModel, quickPanel, logger);

            mainViewModel.BoxesChanged += async (_, _) => await _desktopBoxManager.RefreshAsync();
            mainViewModel.ItemsChanged += async (_, _) =>
            {
                await quickPanelViewModel.LoadAsync();
                await _desktopBoxManager.RefreshAsync();
            };
            _desktopBoxManager.ItemsChanged += async (_, _) =>
            {
                await mainViewModel.LoadAsync();
                await quickPanelViewModel.LoadAsync();
            };
            _mainWindow.Closed += (_, _) => _desktopBoxManager.CloseAll();

            InitializeTaskbarIcon(paths, logger);

            MainWindow = _mainWindow;
            _mainWindow.Show();
            await mainViewModel.LoadAsync();
            await quickPanelViewModel.LoadAsync();
            await _desktopBoxManager.RefreshAsync();
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

    private void InitializeTaskbarIcon(AppPaths paths, IAppLogger logger)
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        _taskbarIcon = new TaskbarIcon(nint.Zero, iconPath, "WitchDrawer");

        _taskbarIcon.LeftClick += (_, _) =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            if (_mainWindow.IsVisible)
            {
                _mainWindow.MinimizeToTray();
            }
            else
            {
                _mainWindow.RestoreFromTray();
            }
        };

        _taskbarIcon.RightClick += (_, _) =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            var menu = CreatePopupMenu();
            var showOrHideText = _mainWindow.IsVisible ? "隐藏主窗口" : "显示主窗口";
            AppendMenuW(menu, 0, 1, showOrHideText);
            AppendMenuW(menu, 0, 2, "退出 WitchDrawer");

            var pt = GetCursorPosition();
            _taskbarIcon.ShowContextMenu(menu, pt.X, pt.Y);
            DestroyMenu(menu);
        };

        _taskbarIcon.MenuCommand += (_, e) =>
        {
            switch (e.CommandId)
            {
                case 1:
                    if (_mainWindow is null)
                    {
                        return;
                    }

                    if (_mainWindow.IsVisible)
                    {
                        _mainWindow.MinimizeToTray();
                    }
                    else
                    {
                        _mainWindow.RestoreFromTray();
                    }
                    break;
                case 2:
                    PerformShutdown();
                    break;
            }
        };

        _taskbarIcon.Show();
    }

    private void PerformShutdown()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
        _desktopBoxManager?.CloseAll();

        if (_mainWindow is not null)
        {
            _mainWindow.ForceClose();
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarIcon?.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static POINT GetCursorPosition()
    {
        GetCursorPos(out var pt);
        return pt;
    }
}
