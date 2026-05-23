using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly DesktopBoxLayoutSettings _layoutSettings;
    private Box _box;
    private bool _isBusy;
    private string _statusText = "拖入文件";

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger,
        DesktopBoxLayoutSettings layoutSettings)
    {
        _box = box;
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
        _layoutSettings = layoutSettings;

        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public DesktopBoxLayoutSettings LayoutSettings => _layoutSettings;

    public event EventHandler? ItemsChanged;

    public ObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public Guid BoxId => _box.Id;

    public string Name => _box.Name;

    public BoxType Type => _box.Type;

    public string TypeLabel => _box.Type switch
    {
        BoxType.Normal => "普通",
        BoxType.Mapping => "映射",
        BoxType.Pixel => "像素",
        _ => "未知"
    };

    public string Description => _box.Type switch
    {
        BoxType.Normal => "移动收纳",
        BoxType.Mapping => "路径映射",
        BoxType.Pixel => "像素收纳",
        _ => string.Empty
    };

    public string ItemCountLabel => $"{Items.Count} 项";

    public bool IsEmpty => Items.Count == 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void UpdateBox(Box box)
    {
        _box = box;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task LoadAsync()
    {
        try
        {
            var items = await _drawerService.GetItemsAsync(BoxId);
            var isPixelated = Type == BoxType.Pixel;

            var existingIds = new HashSet<Guid>();
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                existingIds.Add(Items[i].Id);
            }

            var newIds = items.Select(i => i.Id).ToHashSet();

            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(Items[i].Id))
                {
                    Items.RemoveAt(i);
                }
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (existingIds.Contains(items[i].Id))
                {
                    var existing = Items.FirstOrDefault(x => x.Id == items[i].Id);
                    existing?.ReloadIconIfNeeded();
                    continue;
                }

                Items.Insert(i, new DrawerItemViewModel(items[i], Name, isPixelated));
            }

            StatusText = Items.Count == 0 ? "拖入文件" : "已同步";
            OnPropertyChanged(nameof(ItemCountLabel));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load desktop box.");
            StatusText = exception.Message;
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var pathList = paths.ToArray();
        if (pathList.Length == 0 || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var imported = 0;
            foreach (var path in pathList)
            {
                await _drawerService.ImportPathAsync(BoxId, path);
                imported++;
            }

            await LoadAsync();
            StatusText = $"已收纳 {imported} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import into desktop box.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CompleteDragOutAsync(DrawerItemViewModel? item)
    {
        return DeleteItemAsync(item);
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
            _logger.Error(exception, "Failed to open desktop box item.");
            StatusText = exception.Message;
        }
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _drawerService.DeleteItemAsync(item.Id, _trash);
            await LoadAsync();
            StatusText = $"已移除 {item.DisplayName}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to delete desktop box item.");
            StatusText = exception.Message;
        }
    }
}

