using System.Windows;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed class DesktopBoxManager
{
    private readonly DrawerService _drawerService;
    private readonly IFileLauncher _launcher;
    private readonly IFileTrash _trash;
    private readonly IAppLogger _logger;
    private readonly Dictionary<Guid, DesktopBoxWindow> _windows = [];
    private bool _closing;

    public DesktopBoxManager(
        DrawerService drawerService,
        IFileLauncher launcher,
        IFileTrash trash,
        IAppLogger logger)
    {
        _drawerService = drawerService;
        _launcher = launcher;
        _trash = trash;
        _logger = logger;
    }

    public event EventHandler? ItemsChanged;

    public async Task RefreshAsync()
    {
        if (_closing)
        {
            return;
        }

        var boxes = await _drawerService.GetBoxesAsync();
        var boxIds = boxes.Select(box => box.Id).ToHashSet();

        foreach (var removedId in _windows.Keys.Where(id => !boxIds.Contains(id)).ToArray())
        {
            _windows[removedId].ForceClose();
            _windows.Remove(removedId);
        }

        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];
            if (!_windows.TryGetValue(box.Id, out var window))
            {
                var viewModel = new DesktopBoxViewModel(box, _drawerService, _launcher, _trash, _logger);
                viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(this, EventArgs.Empty);

                window = new DesktopBoxWindow(viewModel);
                PlaceNewWindow(window, index);
                _windows.Add(box.Id, window);
                window.Show();
            }
            else
            {
                window.ViewModel.UpdateBox(box);
                if (!window.IsVisible)
                {
                    window.Show();
                }
            }

            await window.ViewModel.LoadAsync();
        }
    }

    public void CloseAll()
    {
        _closing = true;
        foreach (var window in _windows.Values)
        {
            window.ForceClose();
        }

        _windows.Clear();
    }

    private static void PlaceNewWindow(Window window, int index)
    {
        const double margin = 18;
        const double gap = 12;

        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Right - window.Width - margin;
        window.Top = workArea.Top + 84 + index * (window.Height + gap);

        if (window.Top + window.Height > workArea.Bottom - margin)
        {
            window.Top = workArea.Top + margin;
            window.Left = Math.Max(workArea.Left + margin, window.Left - window.Width - gap);
        }
    }
}

