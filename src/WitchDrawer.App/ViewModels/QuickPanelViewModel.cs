using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class QuickPanelViewModel : ObservableObject
{
    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private List<DrawerItemViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private string _statusText = "快速面板";

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
                ApplyFilter();
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

            ApplyFilter();
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

        Items.Clear();
        foreach (var item in filtered.Take(300))
        {
            Items.Add(item);
        }

        StatusText = $"{Items.Count} / {_allItems.Count} 项";
    }
}

