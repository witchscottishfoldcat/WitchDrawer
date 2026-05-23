using System.Collections.ObjectModel;
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
    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private BoxViewModel? _selectedBox;
    private bool _isBusy;
    private bool _isSettingsPage;
    private string _statusText = "准备就绪";
    private string _themeLabel = "清透";

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
        ApplyMoeThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Moe));
        ApplyGlassThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Glass));
        ApplyCrystalThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Crystal));
        ShowDashboardCommand = new RelayCommand(() => IsSettingsPage = false);
        ShowSettingsCommand = new RelayCommand(() => IsSettingsPage = true);
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

    public IRelayCommand ApplyMoeThemeCommand { get; }

    public IRelayCommand ApplyGlassThemeCommand { get; }

    public IRelayCommand ApplyCrystalThemeCommand { get; }

    public IRelayCommand ShowDashboardCommand { get; }

    public IRelayCommand ShowSettingsCommand { get; }

    public BoxViewModel? SelectedBox
    {
        get => _selectedBox;
        set
        {
            if (SetProperty(ref _selectedBox, value))
            {
                DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
                RenameSelectedBoxCommand.NotifyCanExecuteChanged();
                _ = LoadItemsForSelectedBoxAsync();
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

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var existingSelection = SelectedBox?.Id;
            var boxes = await _drawerService.GetBoxesAsync();

            Boxes.Clear();
            foreach (var box in boxes)
            {
                Boxes.Add(new BoxViewModel(box));
            }

            SelectedBox = Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault();
            if (SelectedBox is not null)
            {
                await LoadItemsForSelectedBoxAsync();
            }

            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        if (SelectedBox is null)
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
                await _drawerService.ImportPathAsync(SelectedBox.Id, path);
                imported++;
            }

            await LoadItemsForSelectedBoxAsync();
            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已导入 {imported} 项到 {SelectedBox.Name}";
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
            var viewModel = new BoxViewModel(box);
            Boxes.Add(viewModel);
            SelectedBox = viewModel;
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
                Boxes.Add(new BoxViewModel(box));
            }

            SelectedBox = Boxes.FirstOrDefault();
            if (SelectedBox is not null)
            {
                await LoadItemsForSelectedBoxAsync();
            }
            else
            {
                Items.Clear();
            }

            await _quickPanelViewModel.LoadAsync();
            StatusText = $"\u5df2\u5220\u9664 {deletedName}\uff0c\u6536\u7eb3\u680f\u5df2\u79fb\u9664";
            DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
            RenameSelectedBoxCommand.NotifyCanExecuteChanged();
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
                Boxes.Add(new BoxViewModel(box));
            }

            SelectedBox = Boxes.FirstOrDefault(b => b.Id == selectedBox.Id) ?? Boxes.FirstOrDefault();
            if (SelectedBox is not null)
            {
                await LoadItemsForSelectedBoxAsync();
            }

            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已重命名收纳盒为 {newName.Trim()}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadItemsForSelectedBoxAsync()
    {
        var selectedBox = SelectedBox;

        if (selectedBox is null)
        {
            Items.Clear();
            return;
        }

        try
        {
            var items = await _drawerService.GetItemsAsync(selectedBox.Id);
            var isPixelated = selectedBox.Type == BoxType.Pixel;
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new DrawerItemViewModel(item, selectedBox.Name, isPixelated));
            }
        }
        catch (Exception exception)
        {
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
            await LoadItemsForSelectedBoxAsync();
            await _quickPanelViewModel.LoadAsync();
            StatusText = $"已移除 {item.DisplayName}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ApplyTheme(AppTheme theme)
    {
        AppThemeManager.Apply(theme);
        ThemeLabel = theme == AppTheme.Glass ? "黑曜石毛玻璃" : (theme == AppTheme.Crystal ? "全透水晶玻璃" : "冰莓雅致浅色");
        StatusText = $"已切换到 {ThemeLabel} 风格";
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
}
