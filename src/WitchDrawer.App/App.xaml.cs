using System.IO;
using System.IO.Pipes;
using System.Text;
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
    private const string SingleInstanceMutexName = @"Local\WitchDrawer.SingleInstance";
    private const string SingleInstancePipeName = "WitchDrawer.SingleInstance";
    private const string ActivateInstanceCommand = "activate";
    private const string ThemeSettingKey = "Theme";

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _singleInstancePipeCts;
    private TaskbarIcon? _taskbarIcon;
    private MainWindow? _mainWindow;
    private DesktopBoxManager? _desktopBoxManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            await SignalExistingInstanceAsync();
            Shutdown(0);
            return;
        }

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
            AppThemeManager.Apply(await LoadSavedThemeAsync(drawerService));

            var quickPanelViewModel = new QuickPanelViewModel(drawerService, launcher, logger);
            var quickPanel = new QuickPanelWindow(quickPanelViewModel);
            var mainViewModel = new MainViewModel(drawerService, launcher, trash, logger, quickPanelViewModel);
            _desktopBoxManager = new DesktopBoxManager(drawerService, launcher, trash, logger);
            _mainWindow = new MainWindow(mainViewModel, quickPanel, logger);
            StartSingleInstanceServer(logger);

            mainViewModel.BoxesChanged += async (_, _) => await _desktopBoxManager.RefreshAsync();
            mainViewModel.ItemsChanged += async (_, _) =>
            {
                await quickPanelViewModel.LoadAsync();
                await _desktopBoxManager.RefreshAsync();
            };
            _desktopBoxManager.ItemsChanged += async (_, _) =>
            {
                await _desktopBoxManager.RefreshAsync();
                await mainViewModel.LoadAsync();
                await quickPanelViewModel.LoadAsync();
            };
            _mainWindow.Closed += async (_, _) => await _desktopBoxManager.CloseAllAsync();

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

    private static async Task<AppTheme> LoadSavedThemeAsync(DrawerService drawerService)
    {
        var savedTheme = await drawerService.GetSettingAsync(ThemeSettingKey);
        return Enum.TryParse<AppTheme>(savedTheme, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.Moe;
    }

    private void StartSingleInstanceServer(IAppLogger logger)
    {
        _singleInstancePipeCts = new CancellationTokenSource();
        var cancellationToken = _singleInstancePipeCts.Token;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        SingleInstancePipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var command = await reader.ReadLineAsync(cancellationToken);
                    if (string.Equals(command, ActivateInstanceCommand, StringComparison.OrdinalIgnoreCase))
                    {
                        await Dispatcher.InvokeAsync(ActivateExistingMainWindow);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.Error(exception, "Single-instance pipe server failed.");
                    await Task.Delay(250, cancellationToken);
                }
            }
        }, cancellationToken);
    }

    private static async Task SignalExistingInstanceAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    SingleInstancePipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
                await client.ConnectAsync(timeoutCts.Token);
                await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                await writer.WriteLineAsync(ActivateInstanceCommand);
                return;
            }
            catch
            {
                await Task.Delay(120);
            }
        }
    }

    private void ActivateExistingMainWindow()
    {
        _mainWindow?.RestoreFromTray();
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
        _desktopBoxManager?.CloseAllAsync().GetAwaiter().GetResult();

        if (_mainWindow is not null)
        {
            _mainWindow.ForceClose();
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstancePipeCts?.Cancel();
        _singleInstancePipeCts?.Dispose();
        _taskbarIcon?.Dispose();
        _singleInstanceMutex?.Dispose();
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
