using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
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
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const double ItemIconSizeDip = 19;
    private const string ThemeSettingKey = "Theme";
    internal const string ThemeBoxOpacitySettingKeyPrefix = "ThemeBoxOpacity.";
    internal const string BoxBorderOpacitySettingKeyPrefix = "DesktopBoxBorderOpacity.";
    internal const string IconFrameOpacitySettingKeyPrefix = "DesktopIconFrameOpacity.";
    internal const string ThemeBoxOpacityMigrationVersionSettingKey = "ThemeBoxOpacityVersion";
    private const string ThemeBoxOpacityMigrationVersion = "2";
    internal const string EditorFollowsBoxOpacitySettingKey = "EditorFollowsBoxOpacity";
    internal const string DesktopDoubleClickSettingKey = "DesktopDoubleClickToggle";
    internal const string IconToolTipCompactSettingKey = "IconToolTipCompact";
    internal const string AboutPageShownSettingKey = "AboutPageShown";
    private const string StartupRegistryKeyName = "WitchDrawer";

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IShellChangeNotifier _shellChangeNotifier;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private readonly UpdateService _updateService;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private readonly AppPaths _appPaths;
    private readonly DataStorageMigrationService _dataStorageMigrationService;
    private readonly DiagnosticLogExportService _diagnosticLogExportService;
    private BoxViewModel? _selectedBox;
    private CancellationTokenSource? _itemsLoadCts;
    private int _itemsLoadVersion;
    private bool _isBusy;
    private bool _pendingDesktopReload;
    private Guid? _pendingDesktopReloadBoxId;
    private bool _isSettingsPage;
    private bool _isAboutPage;
    private bool _isArchivePage;
    private string _statusText = "准备就绪";
    private string _themeLabel = "清透雅致";
    private AppTheme _currentTheme;
    private double _themeTransparencyPercent = (1 - AppThemeManager.DefaultBoxOpacity) * 100;
    private double _boxBorderTransparencyPercent;
    private double _iconFrameTransparencyPercent;
    private readonly object _themeOpacitySaveLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _themeOpacitySaveDelays = [];
    private readonly SemaphoreSlim _themeOpacityWriteGate = new(1, 1);
    private bool _isSynchronizingThemeTransparency;
    private bool _editorFollowsBoxOpacity;
    private bool _iconToolTipCompact;
    private readonly AutoHideSettingsStore _autoHideSettingsStore;
    private bool _autoHideEnabled;
    private int _autoHideHiddenTransparencyPercent = AutoHideSettings.DefaultHiddenTransparencyPercent;
    private AutoHideRevealScope _autoHideRevealScope = AutoHideRevealScope.HoveredBoxOnly;
    private bool _autoHideFadeWholeBox = true;
    private bool _autoHideFadeTitle = true;
    private bool _autoHideFadeBorder = true;
    private CancellationTokenSource? _autoHideSaveCts;
    private bool _launchOnStartup;
    private bool _areDesktopIconsHidden;
    private bool _isDesktopDoubleClickEnabled;
    private string _updateStatusText = string.Empty;
    private bool _isCheckingUpdate;
    private string? _pendingUpdateSha256;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;

    public MainViewModel(
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IShellChangeNotifier shellChangeNotifier,
        IAppLogger logger,
        QuickPanelViewModel quickPanelViewModel,
        UpdateService updateService,
        BoxVisualStyleStore boxVisualStyleStore,
        BoxPositionLockStateStore boxPositionLockStateStore,
        AppPaths appPaths,
        DataStorageMigrationService dataStorageMigrationService,
        AutoHideSettingsStore autoHideSettingsStore)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _shellChangeNotifier = shellChangeNotifier;
        _logger = logger;
        _quickPanelViewModel = quickPanelViewModel;
        _updateService = updateService;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        _appPaths = appPaths;
        _dataStorageMigrationService = dataStorageMigrationService;
        _diagnosticLogExportService = new DiagnosticLogExportService(appPaths);
        _autoHideSettingsStore = autoHideSettingsStore;
        TodoBoxDetail = new TodoBoxDetailViewModel(todoService, logger);
        TodoBoxDetail.ItemsChanged += OnTodoBoxDetailItemsChanged;
        BoxSizeSettings = new BoxSizeSettingsViewModel(drawerService, logger);

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
        RestoreArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(RestoreArchivedTodoAsync);
        DeleteArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(DeleteArchivedTodoAsync);
        UndoArchivedDeleteCommand = new AsyncRelayCommand(UndoArchivedDeleteAsync, () => !IsBusy && ArchiveUndo.IsAvailable);
        ArchiveUndo.AvailabilityChanged += (_, _) => UndoArchivedDeleteCommand.NotifyCanExecuteChanged();
        SetCurrentTheme(AppThemeManager.CurrentTheme);

        ApplyMoeThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Moe));
        ApplyGlassThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Glass));
        ApplyCrystalThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Crystal));
        ResetThemeTransparencyCommand = new RelayCommand(ResetThemeTransparency);
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
        ToggleDesktopIconsCommand = new AsyncRelayCommand(ToggleDesktopIconsAsync);
        ToggleDesktopDoubleClickCommand = new AsyncRelayCommand(ToggleDesktopDoubleClickAsync);
        ToggleEditorOpacityFollowCommand = new AsyncRelayCommand(ToggleEditorOpacityFollowAsync);
        ToggleIconToolTipCompactCommand = new AsyncRelayCommand(ToggleIconToolTipCompactAsync);
        ToggleAutoHideEnabledCommand = new AsyncRelayCommand(ToggleAutoHideEnabledAsync);
        ApplyAutoHideScopeHoveredOnlyCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.HoveredBoxOnly));
        ApplyAutoHideScopeAllCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.AllBoxes));
        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
        ShowDashboardCommand = new RelayCommand(() =>
        {
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = false;
        });
        ShowArchiveCommand = new AsyncRelayCommand(ShowArchiveAsync);
        ShowSettingsCommand = new RelayCommand(() =>
        {
            SelectedBox = null;
            AreDesktopIconsHidden = DesktopIconVisibility.IsHidden();
            IsArchivePage = false;
            IsSettingsPage = true;
            IsAboutPage = false;
        });
        ShowAboutCommand = new RelayCommand(() =>
        {
            SelectedBox = null;
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = true;
        });
    }

    public event EventHandler? BoxesChanged;

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;

    public ObservableCollection<BoxViewModel> Boxes { get; } = [];

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<ArchivedTodoItemViewModel> ArchivedTodos { get; } = [];

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

    public IAsyncRelayCommand<ArchivedTodoItemViewModel?> RestoreArchivedTodoCommand { get; }

    public IAsyncRelayCommand<ArchivedTodoItemViewModel?> DeleteArchivedTodoCommand { get; }

    public IAsyncRelayCommand UndoArchivedDeleteCommand { get; }

    public TodoUndoViewModel ArchiveUndo { get; } = new();

    public IAsyncRelayCommand ApplyMoeThemeCommand { get; }

    public IAsyncRelayCommand ApplyGlassThemeCommand { get; }

    public IAsyncRelayCommand ApplyCrystalThemeCommand { get; }

    public IRelayCommand ResetThemeTransparencyCommand { get; }

    public IAsyncRelayCommand ToggleLaunchOnStartupCommand { get; }

    public IAsyncRelayCommand ToggleDesktopIconsCommand { get; }

    public IAsyncRelayCommand ToggleDesktopDoubleClickCommand { get; }

    public IAsyncRelayCommand ToggleEditorOpacityFollowCommand { get; }

    public IAsyncRelayCommand ToggleIconToolTipCompactCommand { get; }

    public IAsyncRelayCommand ToggleAutoHideEnabledCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeHoveredOnlyCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeAllCommand { get; }

    public IAsyncRelayCommand CheckForUpdateCommand { get; }

    public IRelayCommand ShowDashboardCommand { get; }

    public IAsyncRelayCommand ShowArchiveCommand { get; }

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

    public bool IsSelectedTodoBox => SelectedBox?.IsTodoBox == true;

    public bool CanImportFiles => SelectedBox is { IsTodoBox: false };

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

    public bool IsArchivePage
    {
        get => _isArchivePage;
        set => SetProperty(ref _isArchivePage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    internal void ReportStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
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

    public double ThemeTransparencyPercent
    {
        get => _themeTransparencyPercent;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var normalized = Math.Clamp(
                Math.Round(value),
                0,
                (1 - AppThemeManager.MinimumBoxOpacity) * 100);
            if (SetProperty(ref _themeTransparencyPercent, normalized))
            {
                OnPropertyChanged(nameof(ThemeTransparencyLabel));
                var opacity = 1 - (normalized / 100);
                if (!_isSynchronizingThemeTransparency)
                {
                    AppThemeManager.SetBoxOpacity(CurrentTheme, opacity);
                    SynchronizeThemeTransparency();
                    QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(CurrentTheme), FormatOpacity(opacity));
                }
            }
        }
    }

    public string ThemeTransparencyLabel => $"{ThemeTransparencyPercent:0}%";

    public double BoxBorderTransparencyPercent
    {
        get => _boxBorderTransparencyPercent;
        set => SetAppearanceTransparency(ref _boxBorderTransparencyPercent, value,
            nameof(BoxBorderTransparencyPercent), BoxBorderOpacitySettingKeyPrefix,
            AppThemeManager.SetBoxBorderOpacity);
    }

    public double IconFrameTransparencyPercent
    {
        get => _iconFrameTransparencyPercent;
        set => SetAppearanceTransparency(ref _iconFrameTransparencyPercent, value,
            nameof(IconFrameTransparencyPercent), IconFrameOpacitySettingKeyPrefix,
            AppThemeManager.SetIconFrameOpacity);
    }

    public bool EditorFollowsBoxOpacity
    {
        get => _editorFollowsBoxOpacity;
        private set => SetProperty(ref _editorFollowsBoxOpacity, value);
    }

    /// <summary>
    /// 图标名称（悬停提示）显示模式。<see langword="false"/> = 完整显示（文件路径），
    /// <see langword="true"/> = 精简显示（文件名，快捷方式自动去掉 .lnk）。
    /// </summary>
    public bool IconToolTipCompact
    {
        get => _iconToolTipCompact;
        private set => SetProperty(ref _iconToolTipCompact, value);
    }

    public bool AutoHideEnabled
    {
        get => _autoHideEnabled;
        private set => SetProperty(ref _autoHideEnabled, value);
    }

    public int AutoHideHiddenTransparencyPercent
    {
        get => _autoHideHiddenTransparencyPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _autoHideHiddenTransparencyPercent, clamped))
            {
                return;
            }

            // 立即应用（拖动滑块时实时预览），仅持久化走防抖。
            PublishAutoHideSettings();
            QueueAutoHideSave();
        }
    }

    public AutoHideRevealScope AutoHideRevealScope
    {
        get => _autoHideRevealScope;
        private set
        {
            if (!SetProperty(ref _autoHideRevealScope, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAutoHideScopeHoveredOnly));
            OnPropertyChanged(nameof(IsAutoHideScopeAll));
        }
    }

    public bool IsAutoHideScopeHoveredOnly => AutoHideRevealScope == AutoHideRevealScope.HoveredBoxOnly;

    public bool IsAutoHideScopeAll => AutoHideRevealScope == AutoHideRevealScope.AllBoxes;

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒外壳（背景/边框/阴影）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeWholeBox
    {
        get => _autoHideFadeWholeBox;
        set
        {
            if (SetProperty(ref _autoHideFadeWholeBox, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒标题一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeTitle
    {
        get => _autoHideFadeTitle;
        set
        {
            if (SetProperty(ref _autoHideFadeTitle, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒边框（描边）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeBorder
    {
        get => _autoHideFadeBorder;
        set
        {
            if (SetProperty(ref _autoHideFadeBorder, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        private set => SetProperty(ref _launchOnStartup, value);
    }

    public bool AreDesktopIconsHidden
    {
        get => _areDesktopIconsHidden;
        private set => SetProperty(ref _areDesktopIconsHidden, value);
    }

    public bool IsDesktopDoubleClickEnabled
    {
        get => _isDesktopDoubleClickEnabled;
        private set => SetProperty(ref _isDesktopDoubleClickEnabled, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set => SetProperty(ref _isCheckingUpdate, value);
    }

    public string CurrentVersionText
    {
        get
        {
            var version = GetCurrentVersion();
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// 当前生效的数据根目录（数据库与收纳盒文件所在位置）。
    /// </summary>
    public string CurrentDataDirectory => _appPaths.RootDirectory;

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

    private static double? ParseAppearanceOpacity(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
        && double.IsFinite(opacity) && opacity >= 0 && opacity <= 1 ? opacity : null;

    public event EventHandler<UpdateCheckResult>? UpdateRequested;
    public event EventHandler? UpdateConfirmed;
}
