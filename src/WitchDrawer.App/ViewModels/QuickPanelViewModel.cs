using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class QuickPanelViewModel : ObservableObject
{
    private const int MaxVisibleItems = 300;
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(150);

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private List<DrawerItemViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private string _statusText = "快速面板";
    private CancellationTokenSource? _filterCts;

    public QuickPanelViewModel(DrawerService drawerService, IFileLauncher launcher, IAppLogger logger)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _logger = logger;
        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
    }

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                // Debounce so each keystroke does not run a synchronous full scan on the UI
                // thread. The previous pending filter (if any) is cancelled and replaced.
                _filterCts?.Cancel();
                _filterCts = new CancellationTokenSource();
                _ = ApplyFilterDebouncedAsync(_filterCts.Token);
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var boxNames = boxes.ToDictionary(box => box.Id, box => box.Name);
            var items = await _drawerService.GetAllItemsAsync();

            _allItems = items
                .Select(item => new DrawerItemViewModel(
                    item,
                    boxNames.TryGetValue(item.BoxId, out var boxName) ? boxName : string.Empty))
                .ToList();

            await ApplyFilterAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load quick panel.");
            StatusText = exception.Message;
        }
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
            StatusText = $"已打开 {item.DisplayName}";
        }
        catch (FileNotFoundException)
        {
            _logger.Error(new FileNotFoundException(item.PathLabel), $"Quick panel item missing on open: {item.DisplayName}");
            StatusText = $"无法打开 {item.DisplayName}，文件可能已被移动或删除";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open item from quick panel.");
            StatusText = $"无法打开 {item.DisplayName}，文件可能已被移动或删除";
        }
    }

    private async Task ApplyFilterDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FilterDebounce, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await ApplyFilterAsync();
    }

    private async Task ApplyFilterAsync()
    {
        var query = SearchText.Trim();
        var snapshot = _allItems;

        // Filter off the UI thread; large allItems lists would otherwise stall typing.
        var filtered = await Task.Run(() =>
        {
            var matching = string.IsNullOrWhiteSpace(query)
                ? snapshot
                : snapshot.Where(item =>
                    item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.PathLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.BoxName.Contains(query, StringComparison.OrdinalIgnoreCase));
            return matching.Take(MaxVisibleItems).ToList();
        });

        Items.Clear();
        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        StatusText = $"{Items.Count} / {_allItems.Count} 项";
    }
}
