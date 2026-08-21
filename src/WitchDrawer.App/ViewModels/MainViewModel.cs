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

public sealed class MainViewModel : ObservableObject
{
    private const double ItemIconSizeDip = 19;
    private const string ThemeSettingKey = "Theme";
    internal const string ThemeBoxOpacitySettingKeyPrefix = "ThemeBoxOpacity.";
    internal const string ThemeBoxOpacityMigrationVersionSettingKey = "ThemeBoxOpacityVersion";
    private const string ThemeBoxOpacityMigrationVersion = "2";
    internal const string EditorFollowsBoxOpacitySettingKey = "EditorFollowsBoxOpacity";
    internal const string DesktopDoubleClickSettingKey = "DesktopDoubleClickToggle";
    internal const string AboutPageShownSettingKey = "AboutPageShown";
    private const string StartupRegistryKeyName = "WitchDrawer";

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private readonly UpdateService _updateService;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private readonly AppPaths _appPaths;
    private readonly DataStorageMigrationService _dataStorageMigrationService;
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
    private readonly object _themeOpacitySaveLock = new();
    private readonly Dictionary<AppTheme, CancellationTokenSource> _themeOpacitySaveDelays = [];
    private bool _isSynchronizingThemeTransparency;
    private bool _editorFollowsBoxOpacity;
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
        IAppLogger logger,
        QuickPanelViewModel quickPanelViewModel,
        UpdateService updateService,
        BoxVisualStyleStore boxVisualStyleStore,
        BoxPositionLockStateStore boxPositionLockStateStore,
        AppPaths appPaths,
        DataStorageMigrationService dataStorageMigrationService)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        _quickPanelViewModel = quickPanelViewModel;
        _updateService = updateService;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        _appPaths = appPaths;
        _dataStorageMigrationService = dataStorageMigrationService;
        TodoBoxDetail = new TodoBoxDetailViewModel(todoService, logger);
        TodoBoxDetail.ItemsChanged += OnTodoBoxDetailItemsChanged;
        BoxSizeSettings = new BoxSizeSettingsViewModel(drawerService, logger);

        LoadCommand = new AsyncRelayCommand(LoadAsync);
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
        SetCurrentTheme(AppThemeManager.CurrentTheme);

        ApplyMoeThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Moe));
        ApplyGlassThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Glass));
        ApplyCrystalThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Crystal));
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
        ToggleDesktopIconsCommand = new AsyncRelayCommand(ToggleDesktopIconsAsync);
        ToggleDesktopDoubleClickCommand = new AsyncRelayCommand(ToggleDesktopDoubleClickAsync);
        ToggleEditorOpacityFollowCommand = new AsyncRelayCommand(ToggleEditorOpacityFollowAsync);
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

    public IAsyncRelayCommand ApplyMoeThemeCommand { get; }

    public IAsyncRelayCommand ApplyGlassThemeCommand { get; }

    public IAsyncRelayCommand ApplyCrystalThemeCommand { get; }

    public IAsyncRelayCommand ToggleLaunchOnStartupCommand { get; }

    public IAsyncRelayCommand ToggleDesktopIconsCommand { get; }

    public IAsyncRelayCommand ToggleDesktopDoubleClickCommand { get; }

    public IAsyncRelayCommand ToggleEditorOpacityFollowCommand { get; }

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
                AppThemeManager.SetBoxOpacity(CurrentTheme, opacity);
                if (!_isSynchronizingThemeTransparency)
                {
                    QueueThemeOpacitySave(CurrentTheme, opacity);
                }
            }
        }
    }

    public string ThemeTransparencyLabel => $"{ThemeTransparencyPercent:0}%";

    public bool EditorFollowsBoxOpacity
    {
        get => _editorFollowsBoxOpacity;
        private set => SetProperty(ref _editorFollowsBoxOpacity, value);
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

    /// <summary>
    /// 将数据目录整体迁移到新文件夹。成功后需重启应用才会切换到新目录。
    /// </summary>
    public async Task MigrateDataDirectoryAsync(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
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

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var existingSelection = SelectedBox?.Id;
            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);

            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault());

            // 必须在首次启动标记写入前判断是否为旧安装，才能让新用户使用二段透明度，
            // 同时让升级用户保留旧主题原本的视觉效果。
            await RestoreThemeBoxOpacitiesAsync();
            var editorOpacityFollowSetting =
                await _drawerService.GetSettingAsync(EditorFollowsBoxOpacitySettingKey);
            EditorFollowsBoxOpacity = bool.TryParse(
                editorOpacityFollowSetting,
                out var editorFollowsBoxOpacity)
                && editorFollowsBoxOpacity;

            var aboutPageShown = await _drawerService.GetSettingAsync(AboutPageShownSettingKey);
            if (!bool.TryParse(aboutPageShown, out var hasShownAboutPage) || !hasShownAboutPage)
            {
                ShowAboutCommand.Execute(null);
                try
                {
                    await _drawerService.SetSettingAsync(AboutPageShownSettingKey, bool.TrueString);
                }
                catch (Exception exception)
                {
                    // The guide is still useful if the preference cannot be persisted;
                    // try again on the next launch instead of failing startup.
                    _logger.Error(exception, "Failed to persist first-launch about-page preference.");
                }
            }

            LaunchOnStartup = ReadStartupRegistry();
            AreDesktopIconsHidden = DesktopIconVisibility.IsHidden();
            var desktopDoubleClickSetting =
                await _drawerService.GetSettingAsync(DesktopDoubleClickSettingKey);
            IsDesktopDoubleClickEnabled =
                bool.TryParse(desktopDoubleClickSetting, out var desktopDoubleClickEnabled)
                && desktopDoubleClickEnabled;
            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Reloads items/quick-panel state without raising BoxesChanged (avoids desktop refresh loops).
    /// </summary>
    public async Task ReloadItemsFromDesktopAsync(Guid? affectedBoxId = null)
    {
        if (IsBusy)
        {
            // 忙时合流而非丢弃：记录一次待刷，忙完补刷；null 表示全量，
            // 一旦出现第二个不同盒子的变更就保持全量。
            _pendingDesktopReloadBoxId = _pendingDesktopReload
                ? (_pendingDesktopReloadBoxId == affectedBoxId ? affectedBoxId : null)
                : affectedBoxId;
            _pendingDesktopReload = true;
            return;
        }

        try
        {
            IsBusy = true;
            if (affectedBoxId is null || SelectedBox?.Id == affectedBoxId.Value)
            {
                await LoadItemsForSelectedBoxAsync(SelectedBox);
            }
            if (IsArchivePage)
            {
                await LoadArchivedTodosAsync();
            }
            if (affectedBoxId is Guid boxId)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
            }
            else
            {
                await _quickPanelViewModel.LoadAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reload items from desktop boxes.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            FlushPendingDesktopReload();
        }
    }

    private void FlushPendingDesktopReload()
    {
        if (!_pendingDesktopReload || IsBusy)
        {
            return;
        }

        _pendingDesktopReload = false;
        var boxId = _pendingDesktopReloadBoxId;
        _pendingDesktopReloadBoxId = null;
        _ = ReloadItemsFromDesktopAsync(boxId);
    }

    public async Task ReorderBoxAsync(Guid draggedBoxId, Guid targetBoxId, bool insertAfter)
    {
        var draggedBox = Boxes.FirstOrDefault(box => box.Id == draggedBoxId);
        var targetBox = Boxes.FirstOrDefault(box => box.Id == targetBoxId);
        if (draggedBox is null || targetBox is null || ReferenceEquals(draggedBox, targetBox))
        {
            return;
        }

        var originalIndex = Boxes.IndexOf(draggedBox);
        var originalOrder = Boxes.Select(box => box.Id).ToArray();
        Boxes.RemoveAt(originalIndex);

        var targetIndex = Boxes.IndexOf(targetBox);
        var insertionIndex = insertAfter ? targetIndex + 1 : targetIndex;
        Boxes.Insert(insertionIndex, draggedBox);

        var reorderedIds = Boxes.Select(box => box.Id).ToArray();
        if (reorderedIds.SequenceEqual(originalOrder))
        {
            return;
        }

        try
        {
            await _drawerService.ReorderBoxesAsync(reorderedIds);
            SelectedBox = draggedBox;
            StatusText = $"已调整“{draggedBox.Name}”的排列位置";
        }
        catch (Exception exception)
        {
            var currentIndex = Boxes.IndexOf(draggedBox);
            if (currentIndex >= 0 && currentIndex != originalIndex)
            {
                Boxes.Move(currentIndex, originalIndex);
            }

            _logger.Error(exception, "Failed to reorder boxes.");
            StatusText = "收纳盒排序保存失败，已恢复原顺序";
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            StatusText = "请先选择一个收纳盒";
            return;
        }

        if (selectedBox.IsTodoBox)
        {
            StatusText = "待办收纳盒请使用任务输入框添加事项";
            return;
        }

        var pathList = paths.ToArray();
        if (pathList.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            // 固定格数盒的硬约束：主窗口导入同样不得超容量（桌面盒拖入路径已有同等约束）。
            // 超容量的项目若入库，会因无格位被分配到固定边界外，在盒内不可见也不可选。
            var pathsToImport = pathList;
            var skippedForCapacity = 0;
            if (selectedBox.SupportsFixedSize && BoxSizeSettings.IsFixedMode)
            {
                var capacity = BoxSizeSettings.FixedColumns * BoxSizeSettings.FixedRows;
                var remaining = Math.Max(0, capacity - Items.Count);
                if (remaining < pathList.Length)
                {
                    pathsToImport = pathList.Take(remaining).ToArray();
                    skippedForCapacity = pathList.Length - pathsToImport.Length;
                }
            }

            var imported = 0;
            foreach (var path in pathsToImport)
            {
                await _drawerService.ImportPathAsync(selectedBox.Id, path);
                imported++;
            }

            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = skippedForCapacity > 0
                ? imported > 0
                    ? $"已导入 {imported} 项到 {selectedBox.Name}，盒子已满（{skippedForCapacity} 项未导入）"
                    : $"{selectedBox.Name} 已满，无法导入"
                : $"已导入 {imported} 项到 {selectedBox.Name}";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
        });
    }

    private Task CreateStyledNormalBoxAsync(BoxVisualStyleOption? option)
    {
        return option is null
            ? Task.CompletedTask
            : CreateBoxAsync(BoxType.Normal, option.Style);
    }

    private async Task CreateBoxAsync(
        BoxType type,
        BoxVisualStyle? visualStyle = null)
    {
        await RunBusyAsync(async () =>
        {
            var prefix = type switch
            {
                BoxType.Normal => "普通收纳盒",
                BoxType.Mapping => "映射收纳盒",
                BoxType.Pixel => "像素收纳盒",
                BoxType.Todo => "待办收纳盒",
                BoxType.Drawer => "抽屉盒",
                _ => "收纳盒"
            };
            var matchingBoxCount = type == BoxType.Normal
                ? Boxes.Count(box => box.Type is BoxType.Normal or BoxType.Pixel)
                : Boxes.Count(box => box.Type == type);
            var name = $"{prefix} {matchingBoxCount + 1}";
            var box = await _drawerService.CreateBoxAsync(name, type);
            if (type == BoxType.Drawer)
            {
                await _drawerService.SetSettingAsync(
                    BoxViewModel.GetLayoutPresetSettingKey(box.Id),
                    DesktopBoxLayoutSettings.DefaultDrawerPreset);
            }
            var effectiveStyle = visualStyle ?? BoxVisualStyle.Modern;
            if (type == BoxType.Normal)
            {
                try
                {
                    await _boxVisualStyleStore.SaveAsync(box.Id, effectiveStyle);
                }
                catch
                {
                    await CompensateFailedStyledBoxCreationAsync(box.Id);
                    throw;
                }
            }

            var viewModel = new BoxViewModel(
                box,
                _drawerService,
                effectiveStyle,
                isPositionLocked: false,
                logger: _logger);
            Boxes.Add(viewModel);
            await SelectBoxAsync(viewModel);
            StatusText = $"已创建 {name}，桌面收纳栏已生成";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task SetSelectedBoxVisualStyleAsync(BoxVisualStyleOption? option)
    {
        var selectedBox = SelectedBox;
        if (option is null || selectedBox?.CanSelectVisualStyle != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _boxVisualStyleStore.SaveAsync(selectedBox.Id, option.Style);
            selectedBox.ApplyVisualStyle(option.Style);
            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = $"已将“{selectedBox.Name}”切换为{option.Name}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task ToggleSelectedBoxPositionLockAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var isPositionLocked = !selectedBox.IsPositionLocked;
            await _boxPositionLockStateStore.SaveAsync(
                selectedBox.Id,
                isPositionLocked);
            selectedBox.ApplyPositionLockState(isPositionLocked);
            WeakReferenceMessenger.Default.Send(
                new BoxPositionLockStateChangedMessage(
                    selectedBox.Id,
                    isPositionLocked));
            StatusText = isPositionLocked
                ? $"已锁定“{selectedBox.Name}”的桌面位置"
                : $"已解锁“{selectedBox.Name}”的桌面位置";
        });
    }

    private async Task CompensateFailedStyledBoxCreationAsync(Guid boxId)
    {
        try
        {
            await _drawerService.DeleteBoxAsync(boxId);
            _logger.Info(
                $"Removed empty box {boxId:N} after visual style persistence failed.");
        }
        catch (Exception compensationException)
        {
            _logger.Error(
                compensationException,
                $"Failed to remove empty box {boxId:N} after visual style persistence failed.");
        }
    }

    private async Task<(Box Box, BoxVisualStyle VisualStyle, bool IsPositionLocked)[]> LoadBoxPresentationAsync(
        IReadOnlyList<Box> boxes)
    {
        return await Task.WhenAll(
            boxes.Select(async box =>
            {
                var visualStyleTask = _boxVisualStyleStore.LoadAsync(box);
                var positionLockStateTask =
                    _boxPositionLockStateStore.LoadAsync(box.Id);
                await Task.WhenAll(visualStyleTask, positionLockStateTask);
                return (
                    Box: box,
                    VisualStyle: await visualStyleTask,
                    IsPositionLocked: await positionLockStateTask);
            }));
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
            var result = await _drawerService.DeleteBoxAsync(selectedBox.Id);

            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(
                result.BoxRemoved
                    ? Boxes.FirstOrDefault()
                    : Boxes.FirstOrDefault(box => box.Id == result.BoxId) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = result.StatusMessage;
            BoxesChanged?.Invoke(this, EventArgs.Empty);
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
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
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(b => b.Id == selectedBox.Id) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
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
        BoxSizeSettings.SetTargetBox(value);
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(IsSelectedTodoBox));
        OnPropertyChanged(nameof(CanImportFiles));
        DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
        RenameSelectedBoxCommand.NotifyCanExecuteChanged();
        SetSelectedBoxVisualStyleCommand.NotifyCanExecuteChanged();
        ToggleSelectedBoxPositionLockCommand.NotifyCanExecuteChanged();
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
        if (selectedBox?.IsTodoBox == true)
        {
            if (IsCurrentItemsLoad(selectedBox, version))
            {
                Items.ReplaceAll([]);
            }

            await TodoBoxDetail.LoadAsync(selectedBox.Id);
            return;
        }

        await TodoBoxDetail.LoadAsync(null);
        if (selectedBox is null)
        {
            if (IsCurrentItemsLoad(null, version))
            {
                Items.ReplaceAll([]);
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

            var isPixelated = selectedBox.IsPixelStyle;
            Items.ReplaceAll(items.Select(item =>
                new DrawerItemViewModel(
                    item,
                    selectedBox.Name,
                    isPixelated,
                    GetIconPixelSize(isPixelated),
                    _logger)));
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
            var result = await _drawerService.DeleteItemAsync(item.Id);
            await LoadItemsForSelectedBoxAsync(SelectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(item.Model.BoxId);
            StatusText = result.StatusMessage;
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(item.Model.BoxId));
        });
    }

    private async Task ShowArchiveAsync()
    {
        SelectedBox = null;
        IsArchivePage = true;
        IsSettingsPage = false;
        IsAboutPage = false;
        await LoadArchivedTodosAsync();
    }

    private async Task RestoreArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _todoService.RestoreArchivedAsync(todo.Id);
            await LoadArchivedTodosAsync();
            if (SelectedBox?.Id == todo.Model.BoxId)
            {
                await TodoBoxDetail.LoadAsync(todo.Model.BoxId);
            }

            StatusText = $"已将“{todo.Title}”恢复到 {todo.BoxName}";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(todo.Model.BoxId));
        });
    }

    private async Task DeleteArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            await LoadArchivedTodosAsync();
            StatusText = $"已删除归档事项“{todo.Title}”";
        });
    }

    private async Task LoadArchivedTodosAsync()
    {
        try
        {
            var archivedTodos = await _todoService.GetArchivedTodosAsync();
            var boxNames = Boxes.ToDictionary(box => box.Id, box => box.Name);

            ArchivedTodos.Clear();
            foreach (var todo in archivedTodos)
            {
                var boxName = boxNames.GetValueOrDefault(todo.BoxId, "待办收纳盒");
                ArchivedTodos.Add(new ArchivedTodoItemViewModel(todo, boxName));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load archived todos.");
            StatusText = exception.Message;
        }
    }

    private void OnTodoBoxDetailItemsChanged(object? sender, EventArgs e)
    {
        StatusText = TodoBoxDetail.StatusText;
        if (TodoBoxDetail.BoxId is Guid boxId)
        {
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
        }
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
        SynchronizeThemeTransparency();
        UpdateThemeLabel();
    }

    private void UpdateThemeLabel()
    {
        ThemeLabel = CurrentTheme switch
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
            FlushPendingDesktopReload();
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

    private async Task ToggleDesktopIconsAsync()
    {
        try
        {
            var hidden = !AreDesktopIconsHidden;
            await DesktopIconVisibility.SetHiddenAsync(hidden);
            AreDesktopIconsHidden = hidden;
            StatusText = hidden ? "已隐藏 Windows 桌面图标" : "已显示 Windows 桌面图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle Windows desktop icons.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopDoubleClickAsync()
    {
        try
        {
            var enabled = !IsDesktopDoubleClickEnabled;
            await _drawerService.SetSettingAsync(
                DesktopDoubleClickSettingKey,
                enabled.ToString());
            IsDesktopDoubleClickEnabled = enabled;
            StatusText = enabled ? "已开启桌面双击切换图标" : "已关闭桌面双击切换图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop double-click setting.");
            OnPropertyChanged(nameof(IsDesktopDoubleClickEnabled));
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
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\" --silent");
            }
        }
        else
        {
            key.DeleteValue(StartupRegistryKeyName, throwOnMissingValue: false);
        }
    }

    private async Task RestoreThemeBoxOpacitiesAsync()
    {
        var migrationVersion =
            await _drawerService.GetSettingAsync(ThemeBoxOpacityMigrationVersionSettingKey);
        if (string.Equals(
                migrationVersion,
                ThemeBoxOpacityMigrationVersion,
                StringComparison.Ordinal))
        {
            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                var savedOpacity = await _drawerService.GetSettingAsync(GetThemeBoxOpacitySettingKey(theme));
                AppThemeManager.SetBoxOpacity(theme, ParseSavedOpacity(savedOpacity));
            }

            SynchronizeThemeTransparency();
            return;
        }

        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
            if (string.Equals(migrationVersion, "1", StringComparison.Ordinal))
            {
                var savedOpacity = ParseSavedOpacity(
                    await _drawerService.GetSettingAsync(GetThemeBoxOpacitySettingKey(theme)));
                if (!IsVersionOneGeneratedDefault(savedOpacity))
                {
                    opacity = savedOpacity;
                }
            }

            AppThemeManager.SetBoxOpacity(theme, opacity);
            await _drawerService.SetSettingAsync(
                GetThemeBoxOpacitySettingKey(theme),
                FormatOpacity(opacity));
        }

        await _drawerService.SetSettingAsync(
            ThemeBoxOpacityMigrationVersionSettingKey,
            ThemeBoxOpacityMigrationVersion);
        SynchronizeThemeTransparency();
    }

    private async Task ToggleEditorOpacityFollowAsync()
    {
        try
        {
            var enabled = !EditorFollowsBoxOpacity;
            await _drawerService.SetSettingAsync(
                EditorFollowsBoxOpacitySettingKey,
                enabled.ToString());
            EditorFollowsBoxOpacity = enabled;
            StatusText = enabled
                ? "编辑页已跟随桌面盒子透明度"
                : "编辑页已保持标准透明度";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save editor opacity follow setting.");
            OnPropertyChanged(nameof(EditorFollowsBoxOpacity));
            StatusText = exception.Message;
        }
    }

    private static bool IsVersionOneGeneratedDefault(double opacity)
    {
        return Math.Abs(opacity - AppThemeManager.DefaultBoxOpacity) < 0.0001
            || Math.Abs(opacity - AppThemeManager.MaximumBoxOpacity) < 0.0001;
    }

    private void SynchronizeThemeTransparency()
    {
        _isSynchronizingThemeTransparency = true;
        try
        {
            ThemeTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetBoxOpacity(CurrentTheme)) * 100);
        }
        finally
        {
            _isSynchronizingThemeTransparency = false;
        }
    }

    private void QueueThemeOpacitySave(AppTheme theme, double opacity)
    {
        CancellationTokenSource delay;
        lock (_themeOpacitySaveLock)
        {
            if (_themeOpacitySaveDelays.TryGetValue(theme, out var previousDelay))
            {
                previousDelay.Cancel();
            }

            delay = new CancellationTokenSource();
            _themeOpacitySaveDelays[theme] = delay;
        }

        _ = PersistThemeOpacityAfterDelayAsync(theme, opacity, delay);
    }

    private async Task PersistThemeOpacityAfterDelayAsync(
        AppTheme theme,
        double opacity,
        CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(250, delay.Token);
            await _drawerService.SetSettingAsync(
                GetThemeBoxOpacitySettingKey(theme),
                FormatOpacity(opacity),
                delay.Token);
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested)
        {
            // 连续拖动时只保存停止后的最终值。
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Failed to persist {theme} box opacity.");
        }
        finally
        {
            lock (_themeOpacitySaveLock)
            {
                if (_themeOpacitySaveDelays.TryGetValue(theme, out var currentDelay)
                    && ReferenceEquals(currentDelay, delay))
                {
                    _themeOpacitySaveDelays.Remove(theme);
                }
            }

            delay.Dispose();
        }
    }

    internal static string GetThemeBoxOpacitySettingKey(AppTheme theme)
    {
        return ThemeBoxOpacitySettingKeyPrefix + theme;
    }

    private static string FormatOpacity(double opacity)
    {
        return opacity.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static double ParseSavedOpacity(string? value)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var opacity)
            ? Math.Clamp(
                opacity,
                AppThemeManager.MinimumBoxOpacity,
                AppThemeManager.MaximumBoxOpacity)
            : AppThemeManager.DefaultBoxOpacity;
    }

    private async Task CheckForUpdateAsync()
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在检查更新...";

            var currentVersion = GetCurrentVersion();
            var result = await _updateService.CheckForUpdateAsync(currentVersion);

            if (!result.HasUpdate)
            {
                UpdateStatusText = $"已是最新版本 v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
                StatusText = UpdateStatusText;
                return;
            }

            var versionText = $"v{result.LatestVersion.Major}.{result.LatestVersion.Minor}.{result.LatestVersion.Build}";
            UpdateStatusText = $"发现新版本 {versionText}";
            StatusText = UpdateStatusText;
            _pendingUpdateSha256 = result.ExpectedSha256;

            UpdateRequested?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update check failed.");
            UpdateStatusText = "检查更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public async Task ExecuteUpdateAsync(string downloadUrl)
    {
        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在下载更新...";

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText = $"正在下载更新... {percent}%";
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(
                downloadUrl,
                progress,
                _pendingUpdateSha256);

            if (success)
            {
                UpdateStatusText = "更新下载完成，正在重启...";
                StatusText = UpdateStatusText;
                UpdateConfirmed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UpdateStatusText = "下载更新失败";
                StatusText = UpdateStatusText;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update download failed.");
            UpdateStatusText = "下载更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public event EventHandler<UpdateCheckResult>? UpdateRequested;
    public event EventHandler? UpdateConfirmed;

    private static Version GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }
}
