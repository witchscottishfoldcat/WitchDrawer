using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class QuickPanelViewModel : ObservableObject
{
    private const double ItemIconSizeDip = 30;

    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private List<DrawerItemViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;
    private string _statusText = "快速面板";

    public QuickPanelViewModel(
        DrawerService drawerService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyleStore boxVisualStyleStore)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _logger = logger;
        _boxVisualStyleStore = boxVisualStyleStore;
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

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);

        foreach (var item in _allItems)
        {
            item.RequestIconSize(GetIconPixelSize(item.IsPixelated));
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var boxesById = boxes.ToDictionary(box => box.Id);
            var boxStyles = await Task.WhenAll(
                boxes.Select(async box =>
                    (box.Id, Style: await _boxVisualStyleStore.LoadAsync(box))));
            var stylesByBoxId = boxStyles.ToDictionary(entry => entry.Id, entry => entry.Style);
            var items = await _drawerService.GetAllItemsAsync();

            _allItems = items
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

            ApplyFilter();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load quick panel.");
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

