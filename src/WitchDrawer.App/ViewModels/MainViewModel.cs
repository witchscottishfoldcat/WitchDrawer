using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string ThemeSettingKey = "Theme";
    private const string LayoutPresetSettingKey = "LayoutPreset";
    private const string StartupRegistryKeyName = "WitchDrawer";

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private BoxViewModel? _selectedBox;
    private CancellationTokenSource? _itemsLoadCts;
    private int _itemsLoadVersion;
    private bool _isBusy;
    private bool _isSettingsPage;
    private bool _isAboutPage;
    private string _statusText = "准备就绪";
    private string _themeLabel = "清透雅致";
    private AppTheme _currentTheme;
    private bool _launchOnStartup;

    public MainViewModel(
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger,
        QuickPanelViewModel quickPanelViewModel,
        DesktopBoxLayoutSettings desktopBoxLayoutSettings)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
        _quickPanelViewModel = quickPanelViewModel;
        DesktopBoxLayout = desktopBoxLayoutSettings;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        CreateNormalBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Normal));
        CreateMappingBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Mapping));
        CreatePixelBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Pixel));
        DeleteSelectedBoxCommand = new AsyncRelayCommand(DeleteSelectedBoxAsync, () => SelectedBox is not null);
        RenameSelectedBoxCommand = new AsyncRelayCommand<string?>(RenameSelectedBoxAsync, _ => SelectedBox is not null);
        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        SetCurrentTheme(AppThemeManager.CurrentTheme);

        ApplyMoeThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Moe));
        ApplyGlassThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Glass));
        ApplyCrystalThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Crystal));
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
        ShowDashboardCommand = new RelayCommand(() => { IsSettingsPage = false; IsAboutPage = false; });
        ShowSettingsCommand = new RelayCommand(() => { IsSettingsPage = true; IsAboutPage = false; });
        ShowAboutCommand = new RelayCommand(() => { IsSettingsPage = false; IsAboutPage = true; });
    }

    public event EventHandler? BoxesChanged;

    public event EventHandler? ItemsChanged;

    public ObservableCollection<BoxViewModel> Boxes { get; } = [];

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public DesktopBoxLayoutSettings DesktopBoxLayout { get; }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand CreateNormalBoxCommand { get; }

    public IAsyncRelayCommand CreateMappingBoxCommand { get; }

    public IAsyncRelayCommand CreatePixelBoxCommand { get; }

    public IAsyncRelayCommand DeleteSelectedBoxCommand { get; }

    public IAsyncRelayCommand<string?> RenameSelectedBoxCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand ApplyMoeThemeCommand { get; }

    public IAsyncRelayCommand ApplyGlassThemeCommand { get; }

    public IAsyncRelayCommand ApplyCrystalThemeCommand { get; }

    public IAsyncRelayCommand ToggleLaunchOnStartupCommand { get; }

    public IRelayCommand ShowDashboardCommand { get; }

    public IRelayCommand ShowSettingsCommand { get; }

    public IRelayCommand ShowAboutCommand { get; }

    public BoxViewModel? SelectedBox
    {
        get => _selectedBox;
        set
        {
            if (UpdateSelectedBoxCore(value))
            {
                QueueSelectedBoxItemsLoad();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsSettingsPage
    {
        get => _isSettingsPage;
        set => SetProperty(ref _isSettingsPage, value);
    }

    public bool IsAboutPage
    {
        get => _isAboutPage;
        set => SetProperty(ref _isAboutPage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ThemeLabel
    {
        get => _themeLabel;
        private set => SetProperty(ref _themeLabel, value);
    }

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                OnPropertyChanged(nameof(IsMoeTheme));
                OnPropertyChanged(nameof(IsGlassTheme));
                OnPropertyChanged(nameof(IsCrystalTheme));
            }
        }
    }

    public bool IsMoeTheme => CurrentTheme == AppTheme.Moe;

    public bool IsGlassTheme => CurrentTheme == AppTheme.Glass;

    public bool IsCrystalTheme => CurrentTheme == AppTheme.Crystal;

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        private set => SetProperty(ref _launchOnStartup, value);
    }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var existingSelection = SelectedBox?.Id;
            var boxes = await _drawerService.GetBoxesAsync();

            Boxes.Clear();
            foreach (var box in boxes)
            {
                Boxes.Add(new BoxViewModel(box, _drawerService));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault());

            LaunchOnStartup = ReadStartupRegistry();
            await RestoreLayoutPresetAsync();

            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            StatusText = "请先选择一个收纳盒";
            return;
        }

        var pathList = paths.ToArray();
        if (pathList.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var imported = 0;
            foreach (var path in pathList)
            {
                await _drawerService.ImportPathAsync(selectedBox.Id, path);
                imported++;
            }

            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已导入 {imported} 项到 {selectedBox.Name}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task CreateBoxAsync(BoxType type)
    {
        await RunBusyAsync(async () =>
        {
            var prefix = type switch
            {
                BoxType.Normal => "普通收纳盒",
                BoxType.Mapping => "映射收纳盒",
                BoxType.Pixel => "像素收纳盒",
                _ => "收纳盒"
            };
            var name = $"{prefix} {Boxes.Count(box => box.Type == type) + 1}";
            var box = await _drawerService.CreateBoxAsync(name, type);
            var viewModel = new BoxViewModel(box, _drawerService);
            Boxes.Add(viewModel);
            await SelectBoxAsync(viewModel);
            StatusText = $"已创建 {name}，桌面收纳栏已生成";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task DeleteSelectedBoxAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var deletedName = selectedBox.Name;
            await _drawerService.DeleteBoxAsync(selectedBox.Id, _trash);

            var boxes = await _drawerService.GetBoxesAsync();
            Boxes.Clear();
            foreach (var box in boxes)
            {
                Boxes.Add(new BoxViewModel(box, _drawerService));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault());

            await _quickPanelViewModel.LoadAsync();
            StatusText = $"\u5df2\u5220\u9664 {deletedName}\uff0c\u6536\u7eb3\u680f\u5df2\u79fb\u9664";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task RenameSelectedBoxAsync(string? newName)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null || newName is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.RenameBoxAsync(selectedBox.Id, newName);

            var boxes = await _drawerService.GetBoxesAsync();
            Boxes.Clear();
            foreach (var box in boxes)
            {
                Boxes.Add(new BoxViewModel(box, _drawerService));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(b => b.Id == selectedBox.Id) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已重命名收纳盒为 {newName.Trim()}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool UpdateSelectedBoxCore(BoxViewModel? value)
    {
        if (EqualityComparer<BoxViewModel?>.Default.Equals(_selectedBox, value))
        {
            return false;
        }

        _selectedBox = value;
        OnPropertyChanged(nameof(SelectedBox));
        DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
        RenameSelectedBoxCommand.NotifyCanExecuteChanged();
        return true;
    }

    private async Task SelectBoxAsync(BoxViewModel? box)
    {
        UpdateSelectedBoxCore(box);
        await LoadItemsForSelectedBoxAsync(box);
    }

    private void QueueSelectedBoxItemsLoad()
    {
        var selectedBox = SelectedBox;
        var (version, cancellationToken) = BeginItemsLoad();
        _ = LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private async Task LoadItemsForSelectedBoxAsync(BoxViewModel? selectedBox)
    {
        var (version, cancellationToken) = BeginItemsLoad();
        await LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private (int Version, CancellationToken CancellationToken) BeginItemsLoad()
    {
        _itemsLoadCts?.Cancel();
        _itemsLoadCts = new CancellationTokenSource();

        var version = Interlocked.Increment(ref _itemsLoadVersion);
        return (version, _itemsLoadCts.Token);
    }

    private bool IsCurrentItemsLoad(BoxViewModel? selectedBox, int version)
    {
        return version == Volatile.Read(ref _itemsLoadVersion)
            && SelectedBox?.Id == selectedBox?.Id;
    }

    private async Task LoadItemsForSelectedBoxAsync(
        BoxViewModel? selectedBox,
        int version,
        CancellationToken cancellationToken)
    {
        if (selectedBox is null)
        {
            if (IsCurrentItemsLoad(null, version))
            {
                Items.Clear();
            }

            return;
        }

        try
        {
            var items = await _drawerService.GetItemsAsync(selectedBox.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            var isPixelated = selectedBox.Type == BoxType.Pixel;
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new DrawerItemViewModel(item, selectedBox.Name, isPixelated));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            _logger.Error(exception, "Failed to load drawer items.");
            StatusText = exception.Message;
        }
    }

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = $"已打开 {item.DisplayName}";
        });
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.DeleteItemAsync(item.Id, _trash);
            await LoadItemsForSelectedBoxAsync(SelectedBox);
            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已移除 {item.DisplayName}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            AppThemeManager.Apply(theme);
            SetCurrentTheme(theme);
            await _drawerService.SetSettingAsync(ThemeSettingKey, theme.ToString());
            StatusText = $"已切换到 {ThemeLabel} 风格";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply theme.");
            StatusText = exception.Message;
        }
    }

    private void SetCurrentTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        ThemeLabel = theme switch
        {
            AppTheme.Glass => "暗黑曜石",
            AppTheme.Crystal => "全透水晶",
            _ => "清透雅致"
        };
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
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(StartupRegistryKeyName, throwOnMissingValue: false);
        }
    }

    private async Task RestoreLayoutPresetAsync()
    {
        var savedPreset = await _drawerService.GetSettingAsync(LayoutPresetSettingKey);
        if (!string.IsNullOrEmpty(savedPreset))
        {
            DesktopBoxLayout.ApplyPresetCommand.Execute(savedPreset);
        }
    }

    public async Task SaveLayoutPresetAsync(string preset)
    {
        await _drawerService.SetSettingAsync(LayoutPresetSettingKey, preset);
    }
}
