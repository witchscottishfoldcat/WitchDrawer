using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IBoxContentRefreshTarget
{
    private readonly UiOperationState _operations;
    public SettingsViewModel Settings { get; }
    public UpdateViewModel Updates { get; }
    public ArchiveViewModel Archive { get; }
    public MaintenanceViewModel Maintenance { get; }
    private Task RunBusyAsync(Func<Task> action) => _operations.RunAsync(action);

    private async Task ShowArchiveAsync()
    {
        SelectedBox = null;
        IsArchivePage = true;
        IsSettingsPage = false;
        IsAboutPage = false;
        await Archive.LoadAsync();
    }

    private const double ItemIconSizeDip = 19;
    internal const string AboutPageShownSettingKey = "AboutPageShown";

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IShellChangeNotifier _shellChangeNotifier;
    private readonly IAppLogger _logger;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private BoxViewModel? _selectedBox;
    private CancellationTokenSource? _itemsLoadCts;
    private int _itemsLoadVersion;
    private bool _pendingDesktopReload;
    private BoxRefreshRequest? _pendingDesktopReloadRequest;
    private bool _isSettingsPage;
    private bool _isAboutPage;
    private bool _isArchivePage;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;

    public MainViewModel(
        DrawerService drawerService, TodoService todoService,
        IFileLauncher launcher, IShellChangeNotifier shellChangeNotifier, IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore, BoxPositionLockStateStore boxPositionLockStateStore,
        SettingsViewModel settings, UpdateViewModel updates, ArchiveViewModel archive,
        MaintenanceViewModel maintenance, UiOperationState operations)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _shellChangeNotifier = shellChangeNotifier;
        _logger = logger;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        Settings = settings;
        Updates = updates;
        Archive = archive;
        Maintenance = maintenance;
        _operations = operations;
        operations.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(IsBusy) && !IsBusy) FlushPendingDesktopReload();
        };
        TodoBoxDetail = new TodoBoxDetailViewModel(todoService, logger);
        TodoBoxDetail.ItemsChanged += (_, _) => ReportStatus(TodoBoxDetail.StatusText);
        BoxSizeSettings = new BoxSizeSettingsViewModel(drawerService.Settings, logger);

        LoadCommand = new AsyncRelayCommand(() => LoadAsync());
        CreateNormalBoxCommand = new AsyncRelayCommand(
            () => CreateBoxAsync(BoxType.Normal, BoxVisualStyle.Modern));
        CreateMappingBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Mapping));
        CreatePixelBoxCommand = new AsyncRelayCommand(
            () => CreateBoxAsync(BoxType.Normal, BoxVisualStyle.Pixel));
        CreateStyledNormalBoxCommand =
            new AsyncRelayCommand<BoxVisualStyleOption?>(CreateStyledNormalBoxAsync);
        SetSelectedBoxVisualStyleCommand =
            new AsyncRelayCommand<BoxVisualStyleOption?>(
                SetSelectedBoxVisualStyleAsync,
                option => option is not null && SelectedBox?.CanSelectVisualStyle == true);
        ToggleSelectedBoxPositionLockCommand =
            new AsyncRelayCommand(
                ToggleSelectedBoxPositionLockAsync,
                () => SelectedBox is not null);
        CreateTodoBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Todo));
        CreateDrawerBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Drawer));
        DeleteSelectedBoxCommand = new AsyncRelayCommand(DeleteSelectedBoxAsync, () => SelectedBox is not null);
        RenameSelectedBoxCommand = new AsyncRelayCommand<string?>(RenameSelectedBoxAsync, _ => SelectedBox is not null);
        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        ShowDashboardCommand = new RelayCommand(() =>
        {
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = false;
        });
        ShowArchiveCommand = new AsyncRelayCommand(ShowArchiveAsync);
        ShowSettingsCommand = new AsyncRelayCommand(async () =>
        {
            SelectedBox = null;
            IsArchivePage = false;
            IsSettingsPage = true;
            IsAboutPage = false;
            await Settings.RefreshDesktopStateAsync();
        });
        ShowAboutCommand = new RelayCommand(() =>
        {
            SelectedBox = null;
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = true;
        });
    }

    public event EventHandler<BoxesChangedEventArgs>? BoxesChanged;

    public ResettableObservableCollection<BoxViewModel> Boxes { get; } = [];

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public TodoBoxDetailViewModel TodoBoxDetail { get; }

    public BoxSizeSettingsViewModel BoxSizeSettings { get; }

    public IReadOnlyList<BoxVisualStyleOption> BoxVisualStyleOptions =>
        BoxVisualStyleCatalog.Options;

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);

        foreach (var item in Items)
        {
            item.RequestIconSize(GetIconPixelSize(item.IsPixelated));
        }
    }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand CreateNormalBoxCommand { get; }

    public IAsyncRelayCommand CreateMappingBoxCommand { get; }

    public IAsyncRelayCommand CreatePixelBoxCommand { get; }

    public IAsyncRelayCommand<BoxVisualStyleOption?> CreateStyledNormalBoxCommand { get; }

    public IAsyncRelayCommand<BoxVisualStyleOption?> SetSelectedBoxVisualStyleCommand { get; }

    public IAsyncRelayCommand ToggleSelectedBoxPositionLockCommand { get; }

    public IAsyncRelayCommand CreateTodoBoxCommand { get; }

    public IAsyncRelayCommand CreateDrawerBoxCommand { get; }

    public IAsyncRelayCommand DeleteSelectedBoxCommand { get; }

    public IAsyncRelayCommand<string?> RenameSelectedBoxCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IRelayCommand ShowDashboardCommand { get; }

    public IAsyncRelayCommand ShowArchiveCommand { get; }

    public IAsyncRelayCommand ShowSettingsCommand { get; }

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

    public bool IsSelectedTodoBox => SelectedBox?.IsTodoBox == true;

    public bool CanImportFiles => SelectedBox is { IsTodoBox: false };

    public bool IsBusy
    {
        get => _operations.IsBusy;
        private set => _operations.IsBusy = value;
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

    public bool IsArchivePage
    {
        get => _isArchivePage;
        set => SetProperty(ref _isArchivePage, value);
    }

    public string StatusText
    {
        get => _operations.StatusText;
        private set => _operations.StatusText = value;
    }

    internal void ReportStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    private int GetIconPixelSize(bool isPixelated)
    {
        return DpiAwareIconSize.Calculate(
            ItemIconSizeDip,
            ItemIconSizeDip,
            _iconDpiScaleX,
            _iconDpiScaleY,
            isPixelated);
    }

    private static double NormalizeDpiScale(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }

}
