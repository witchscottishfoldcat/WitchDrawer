using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Native.Files;
using WitchDrawer.Native.Shell;

namespace WitchDrawer.App.Features.ItemContextMenu;

/// <summary>
/// Owns one item-menu session for a desktop box. It isolates menu lifetime and
/// Shell actions from DesktopBoxWindow and delegates every file mutation to the
/// existing view-model/Core command boundary.
/// </summary>
internal sealed class DrawerItemContextMenuCoordinator(DesktopBoxViewModel host) : IDisposable
{
    private DrawerItemContextMenuWindow? _activeMenu;
    private bool _disposed;
    private int _requestVersion;

    public async Task ShowAsync(DrawerItemViewModel item)
    {
        var requestVersion = Interlocked.Increment(ref _requestVersion);
        if (_disposed)
        {
            return;
        }

        try
        {
            CloseActiveMenuCore();
            var path = item.PathLabel;
            var pathState = await Task.Run(() => InspectPath(path));
            if (_disposed || requestVersion != Volatile.Read(ref _requestVersion))
            {
                return;
            }

            if (!pathState.Exists)
            {
                host.ShowFileMissingNotice(item);
                return;
            }

            if (!NativeCursor.TryGetPosition(out var x, out var y))
            {
                return;
            }

            var menu = new DrawerItemContextMenuWindow(
                WindowsFileShellActions.CanRunAsAdministrator(path, pathState.IsDirectory),
                host.IsMappingBox,
                host.IsPixelStyle,
                x,
                y);
            _activeMenu = menu;
            var action = await menu.ShowForSelectionAsync();
            if (ReferenceEquals(_activeMenu, menu))
            {
                _activeMenu = null;
            }

            await ExecuteAsync(action, item, path);
        }
        catch (Exception exception)
        {
            host.ShowContextMenuFailure(item, exception);
        }
    }

    public void CloseActiveMenu()
    {
        Interlocked.Increment(ref _requestVersion);
        CloseActiveMenuCore();
    }

    private void CloseActiveMenuCore()
    {
        var menu = _activeMenu;
        _activeMenu = null;
        if (menu?.IsVisible == true)
        {
            menu.Close();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseActiveMenu();
    }

    internal static (bool Exists, bool IsDirectory) InspectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, false);
        }

        var isDirectory = Directory.Exists(path);
        return (isDirectory || File.Exists(path), isDirectory);
    }

    private async Task ExecuteAsync(
        DrawerItemContextAction action,
        DrawerItemViewModel item,
        string path)
    {
        switch (action)
        {
            case DrawerItemContextAction.Open:
                await host.OpenItemCommand.ExecuteAsync(item);
                break;
            case DrawerItemContextAction.RunAsAdministrator:
                host.ReportItemContextAction(
                    WindowsFileShellActions.TryRunAsAdministrator(path)
                        ? $"已以管理员身份启动 {item.DisplayName}"
                        : "已取消管理员启动");
                break;
            case DrawerItemContextAction.Reveal:
                WindowsFileShellActions.RevealInFileExplorer(path);
                host.ReportItemContextAction($"已定位 {item.DisplayName}");
                break;
            case DrawerItemContextAction.RemoveFromBox:
                await host.DeleteItemCommand.ExecuteAsync(item);
                break;
        }
    }
}
