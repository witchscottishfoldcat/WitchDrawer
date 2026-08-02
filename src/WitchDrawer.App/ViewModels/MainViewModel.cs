using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const double ItemIconSizeDip = 19;
    private const string ThemeSettingKey = "Theme";
    private const string CrystalBoxTransparencySettingKey = "CrystalBoxTransparency";
    private const string StartupRegistryKeyName = "WitchDrawer";

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private readonly UpdateService _updateService;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private BoxViewModel? _selectedBox;
    private CancellationTokenSource? _itemsLoadCts;
    private int _itemsLoadVersion;
    private bool _isBusy;
    private bool _isSettingsPage;
    private bool _isAboutPage;
    private bool _isArchivePage;
    private string _statusText = "准备就绪";
    private string _themeLabel = "清透雅致";
    private AppTheme _currentTheme;
    private bool _isTransparentCrystalBoxes;
    private bool _launchOnStartup;
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
        BoxPositionLockStateStore boxPositionLockStateStore)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        _quickPanelViewModel = quickPanelViewModel;
        _updateService = updateService;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        TodoBoxDetail = new TodoBoxDetailViewModel(todoService, logger);
        TodoBoxDetail.ItemsChanged += OnTodoBoxDetailItemsChanged;

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
        ApplyCrystalThemeCommand = new AsyncRelayCommand(ApplyCrystalThemeAsync);
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
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
            IsArchivePage = false;
            IsSettingsPage = true;
            IsAboutPage = false;
        });
        ShowAboutCommand = new RelayCommand(() =>
        {
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = true;
        });
    }

    public event EventHandler? BoxesChanged;

    public event EventHandler? ItemsChanged;

    public ObservableCollection<BoxViewModel> Boxes { get; } = [];

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<ArchivedTodoItemViewModel> ArchivedTodos { get; } = [];

    public TodoBoxDetailViewModel TodoBoxDetail { get; }

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

    public bool IsTransparentCrystalBoxes
    {
        get => _isTransparentCrystalBoxes;
        private set
        {
            if (SetProperty(ref _isTransparentCrystalBoxes, value))
            {
                UpdateThemeLabel();
            }
        }
    }

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        private set => SetProperty(ref _launchOnStartup, value);
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
                    isPositionLocked));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault());

            LaunchOnStartup = ReadStartupRegistry();
            await RestoreCrystalBoxTransparencyAsync();

            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Reloads items/quick-panel state without raising BoxesChanged (avoids desktop refresh loops).
    /// </summary>
    public async Task ReloadItemsFromDesktopAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadItemsForSelectedBoxAsync(SelectedBox);
            if (IsArchivePage)
            {
                await LoadArchivedTodosAsync();
            }
            await _quickPanelViewModel.LoadAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reload items from desktop boxes.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
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
                isPositionLocked: false);
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
            await _quickPanelViewModel.LoadAsync();
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
                    isPositionLocked));
            }

            await SelectBoxAsync(
                result.BoxRemoved
                    ? Boxes.FirstOrDefault()
                    : Boxes.FirstOrDefault(box => box.Id == result.BoxId) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.LoadAsync();
            StatusText = result.StatusMessage;
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
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked));
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
                Items.Clear();
            }

            await TodoBoxDetail.LoadAsync(selectedBox.Id);
            return;
        }

        await TodoBoxDetail.LoadAsync(null);
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

            var isPixelated = selectedBox.IsPixelStyle;
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new DrawerItemViewModel(
                    item,
                    selectedBox.Name,
                    isPixelated,
                    GetIconPixelSize(isPixelated),
                    _logger));
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
            await _quickPanelViewModel.LoadAsync();
            StatusText = result.StatusMessage;
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task ShowArchiveAsync()
    {
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
            ItemsChanged?.Invoke(this, EventArgs.Empty);
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
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            if (theme != AppTheme.Crystal)
            {
                await SetCrystalBoxTransparencyAsync(false);
            }

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

    private async Task ApplyCrystalThemeAsync()
    {
        if (CurrentTheme == AppTheme.Crystal)
        {
            var useTransparentBoxes = !IsTransparentCrystalBoxes;
            await SetCrystalBoxTransparencyAsync(useTransparentBoxes);
            StatusText = useTransparentBoxes
                ? "桌面收纳盒已切换为透明水晶"
                : "桌面收纳盒已切换为清晰水晶";
            return;
        }

        await SetCrystalBoxTransparencyAsync(false);
        await ApplyThemeAsync(AppTheme.Crystal);
    }

    private async Task SetCrystalBoxTransparencyAsync(bool enabled)
    {
        IsTransparentCrystalBoxes = enabled;
        AppThemeManager.SetCrystalBoxTransparency(enabled);
        await _drawerService.SetSettingAsync(
            CrystalBoxTransparencySettingKey,
            enabled.ToString());
    }

    private void SetCurrentTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        UpdateThemeLabel();
    }

    private void UpdateThemeLabel()
    {
        ThemeLabel = CurrentTheme switch
        {
            AppTheme.Glass => "暗黑曜石",
            AppTheme.Crystal when IsTransparentCrystalBoxes => "全透水晶 · 透明盒",
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
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\" --silent");
            }
        }
        else
        {
            key.DeleteValue(StartupRegistryKeyName, throwOnMissingValue: false);
        }
    }

    private async Task RestoreCrystalBoxTransparencyAsync()
    {
        var savedValue = await _drawerService.GetSettingAsync(CrystalBoxTransparencySettingKey);
        var enabled = CurrentTheme == AppTheme.Crystal
            && bool.TryParse(savedValue, out var savedEnabled)
            && savedEnabled;
        IsTransparentCrystalBoxes = enabled;
        AppThemeManager.SetCrystalBoxTransparency(enabled);
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
