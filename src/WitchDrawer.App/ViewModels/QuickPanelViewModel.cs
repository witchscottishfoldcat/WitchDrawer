using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class QuickPanelViewModel : LocalizedObservableObject, IBoxContentRefreshTarget
{
    private const double ItemIconSizeDip = 30;

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly Func<Task<IReadOnlyList<DrawerItem>>>? _loadItemsOverride;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _hasLoaded;
    private bool _firstLoadInProgress;
    private bool _refreshPendingDuringFirstLoad;
    private List<DrawerItemViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;
    private string _statusText = Strings.Get("QuickPanel");

    public QuickPanelViewModel(
        DrawerService drawerService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore)
        : this(drawerService, launcher, logger, boxVisualStyleStore, null)
    {
    }

    internal QuickPanelViewModel(
        DrawerService drawerService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore,
        Func<Task<IReadOnlyList<DrawerItem>>>? loadItemsOverride)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _logger = logger;
        _boxVisualStyleStore = boxVisualStyleStore;
        _loadItemsOverride = loadItemsOverride;
        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
    }

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    protected override void OnLanguageChanged() => StatusText = _hasLoaded
        ? Strings.Format("Items3", Items.Count, _allItems.Count)
        : Strings.Get("QuickPanel");

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);

        foreach (var item in _allItems)
        {
            item.RequestIconSize(GetIconPixelSize(item.IsPixelated));
        }
    }

    /// <summary>
    /// 首次打开快捷面板时的完整加载入口；之后依赖增量刷新，重复打开不再全量扫描。
    /// 同时到达的加载请求经 _refreshGate 串行合并：后到的请求看到已加载后直接返回。
    /// 尚未开始首次加载时无需记录变更；加载期间的变更会触发一次补读。
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (!_hasLoaded)
            {
                await LoadWithCatchUpAsync();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>显式全量重载（会执行完整扫描）；启动阶段不应再调用。</summary>
    public async Task LoadAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            await LoadWithCatchUpAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// 运行期间的全量刷新：仅在已完成首次加载后执行；
    /// 未初始化时跳过，避免快捷面板从未打开却提前全量扫描。
    /// </summary>
    public async Task RefreshContentAsync(BoxRefreshRequest request)
    {
        if (request.PresentationOnly) await RefreshPresentationAsync(request);
        else if (request.BoxIds is null) await RefreshAllAsync();
        else await RefreshBoxesAsync(request.BoxIds);
    }

    private async Task RefreshPresentationAsync(BoxRefreshRequest request)
    {
        if (!_hasLoaded)
        {
            _refreshPendingDuringFirstLoad |= _firstLoadInProgress;
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var presentations = new Dictionary<Guid, (string Name, bool Pixelated)>();
            foreach (var box in boxes.Where(box => request.Affects(box.Id)))
            {
                var style = await _boxVisualStyleStore.LoadAsync(box);
                presentations.Add(box.Id, (box.Name, style == BoxVisualStyle.Pixel));
            }

            foreach (var item in _allItems)
                if (presentations.TryGetValue(item.Model.BoxId, out var presentation))
                    item.UpdateBoxPresentation(presentation.Name, presentation.Pixelated,
                        GetIconPixelSize(presentation.Pixelated));
            ApplyFilter();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh quick panel presentation.");
            StatusText = exception.Message;
        }
        finally { _refreshGate.Release(); }
    }

    public async Task RefreshAllAsync()
    {
        if (!_hasLoaded)
        {
            _refreshPendingDuringFirstLoad |= _firstLoadInProgress;
            return;
        }

        await LoadAsync();
    }

    private async Task LoadWithCatchUpAsync()
    {
        var isFirstLoad = !_hasLoaded;
        if (isFirstLoad)
        {
            _firstLoadInProgress = true;
        }

        try
        {
            _hasLoaded = await LoadCoreAsync();
            if (isFirstLoad && _hasLoaded && _refreshPendingDuringFirstLoad)
            {
                // Refresh calls made while the first query was in flight were skipped.
                // Read again after marking loaded so later calls wait on _refreshGate.
                _refreshPendingDuringFirstLoad = false;
                _hasLoaded = await LoadCoreAsync();
            }
        }
        finally
        {
            if (isFirstLoad)
            {
                _firstLoadInProgress = false;
                _refreshPendingDuringFirstLoad = false;
            }
        }
    }

    private async Task<bool> LoadCoreAsync()
    {
        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var boxesById = boxes.ToDictionary(box => box.Id);
            var boxStyles = await Task.WhenAll(
                boxes.Select(async box =>
                    (box.Id, Style: await _boxVisualStyleStore.LoadAsync(box))));
            var stylesByBoxId = boxStyles.ToDictionary(entry => entry.Id, entry => entry.Style);
            var items = await (_loadItemsOverride?.Invoke() ?? _drawerService.GetAllItemsAsync());

            _allItems = CreateItemViewModels(items, boxesById, stylesByBoxId);
            ApplyFilter();
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load quick panel.");
            StatusText = exception.Message;
            return false;
        }
    }

    public Task RefreshBoxAsync(Guid boxId) => RefreshBoxesAsync([boxId]);

    private async Task RefreshBoxesAsync(IReadOnlyList<Guid> boxIds)
    {
        if (!_hasLoaded)
        {
            _refreshPendingDuringFirstLoad |= _firstLoadInProgress;
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var affected = boxIds.ToHashSet();
            var existingById = _allItems
                .Where(item => affected.Contains(item.Model.BoxId))
                .ToDictionary(item => item.Id);
            var retainedItems = _allItems
                .Where(item => !affected.Contains(item.Model.BoxId))
                .ToList();

            foreach (var box in boxes.Where(candidate => affected.Contains(candidate.Id)))
            {
                var visualStyle = await _boxVisualStyleStore.LoadAsync(box);
                var items = await _drawerService.GetItemsAsync(box.Id);
                var isPixelated = visualStyle == BoxVisualStyle.Pixel;
                var iconPixelSize = GetIconPixelSize(isPixelated);
                foreach (var item in items)
                {
                    if (existingById.TryGetValue(item.Id, out var existing) && existing.Model == item)
                    {
                        existing.UpdateBoxPresentation(box.Name, isPixelated, iconPixelSize);
                        retainedItems.Add(existing);
                    }
                    else retainedItems.Add(new DrawerItemViewModel(
                        item, box.Name, isPixelated, iconPixelSize, _logger));
                }
            }

            var boxOrder = boxes
                .Select((candidate, index) => (candidate.Id, Index: index))
                .ToDictionary(entry => entry.Id, entry => entry.Index);
            _allItems = retainedItems
                .OrderBy(item => boxOrder.GetValueOrDefault(item.Model.BoxId, int.MaxValue))
                .ThenBy(item => item.Model.SortOrder)
                .ToList();
            ApplyFilter();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh quick panel boxes.");
            StatusText = exception.Message;
        }
        finally
        {
            _refreshGate.Release();
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

        try
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = Strings.Format("Opened", item.DisplayName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open item from quick panel.");
            StatusText = exception.Message;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.PathLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.BoxName.Contains(query, StringComparison.OrdinalIgnoreCase));

        Items.ReplaceAll(filtered.Take(300));
        StatusText = Strings.Format("Items3", Items.Count, _allItems.Count);
    }

    private List<DrawerItemViewModel> CreateItemViewModels(
        IEnumerable<DrawerItem> items,
        IReadOnlyDictionary<Guid, Box> boxesById,
        IReadOnlyDictionary<Guid, BoxVisualStyle> stylesByBoxId)
    {
        return items
            .Select(item =>
            {
                boxesById.TryGetValue(item.BoxId, out var box);
                var isPixelated = stylesByBoxId.TryGetValue(
                    item.BoxId,
                    out var visualStyle)
                    && visualStyle == BoxVisualStyle.Pixel;
                return new DrawerItemViewModel(
                    item,
                    box?.Name ?? string.Empty,
                    isPixelated,
                    GetIconPixelSize(isPixelated),
                    _logger);
            })
            .ToList();
    }
}
