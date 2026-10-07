using System.IO;
using System.Collections.Specialized;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Native.Files;
using WitchDrawer.Native.Shell;

namespace WitchDrawer.App.Features.ItemContextMenu;

internal sealed class DrawerItemContextMenuCoordinator : IDisposable
{
    private readonly DesktopBoxViewModel _host;
    private readonly IFileClipboard _clipboard;
    private readonly Func<bool> _ownerVisible;
    private readonly Func<string?, Task<(bool Exists, bool IsDirectory)>> _inspect;
    private readonly Func<(int X, int Y)?> _cursor;
    private DrawerItemContextMenuWindow? _activeMenu;
    private bool _disposed;
    private bool _requestPending;
    private int _requestVersion;
    private Guid? _requestItemId;

    internal DrawerItemContextMenuCoordinator(DesktopBoxViewModel host, IFileClipboard? clipboard = null,
        Func<bool>? ownerVisible = null,
        Func<string?, Task<(bool Exists, bool IsDirectory)>>? inspect = null,
        Func<(int X, int Y)?>? cursor = null)
    {
        _host = host;
        _clipboard = clipboard ?? new FileClipboard();
        _ownerVisible = ownerVisible ?? (() => true);
        _inspect = inspect ?? (path => Task.Run(() => InspectPath(path)));
        _cursor = cursor ?? (() => NativeCursor.TryGetPosition(out var x, out var y) ? (x, y) : null);
        host.Items.CollectionChanged += OnItemsChanged;
    }

    public bool IsMenuActive => _requestPending || _activeMenu?.IsVisible == true;
    internal bool OwnsWindowHandle(nint handle) => handle != nint.Zero && _activeMenu?.IsVisible == true
        && new System.Windows.Interop.WindowInteropHelper(_activeMenu).Handle == handle;
    public Task ShowAsync(DrawerItemViewModel item) => ShowCoreAsync(item);
    public Task ShowBoxAsync() => ShowCoreAsync(null);

    private async Task ShowCoreAsync(DrawerItemViewModel? item)
    {
        var version = Interlocked.Increment(ref _requestVersion);
        if (_disposed || !_ownerVisible() || _host.IsTodoBox) return;
        CloseActiveMenuCore();
        // Capture the click position before potentially slow path checks.
        var position = _cursor();
        if (position is null) return;
        _requestPending = true;
        _requestItemId = item?.Id;
        try
        {
            var state = item is null ? (Exists: false, IsDirectory: false) : await _inspect(item.PathLabel);
            if (_disposed || !_ownerVisible() || version != Volatile.Read(ref _requestVersion)) return;
            var paths = ReadClipboardPaths();
            var menu = item is null
                ? new DrawerItemContextMenuWindow(false, _host.IsMappingBox, _host.IsPixelStyle,
                    position.Value.X, position.Value.Y, false, paths.Length > 0, hasItem: false, isBusy: _host.IsBusy)
                : CreateMenuForPath(item.PathLabel, state, _host.IsMappingBox, _host.IsPixelStyle,
                    position.Value.X, position.Value.Y, paths.Length > 0, _host.IsBusy);
            _requestPending = false;
            _activeMenu = menu;
            var action = await menu.ShowForSelectionAsync();
            if (ReferenceEquals(_activeMenu, menu)) _activeMenu = null;
            if (_disposed || version != Volatile.Read(ref _requestVersion)) return;
            await ExecuteAsync(action, item);
        }
        catch (Exception exception) { ReportFailure(item, exception); }
        finally
        {
            if (version == Volatile.Read(ref _requestVersion))
            {
                _requestPending = false;
                _requestItemId = null;
            }
        }
    }

    public void CancelPendingMenu() { if (_requestPending) CloseActiveMenu(); }
    public void CloseActiveMenu()
    {
        Interlocked.Increment(ref _requestVersion);
        _requestPending = false;
        _requestItemId = null;
        CloseActiveMenuCore();
    }
    private void CloseActiveMenuCore()
    {
        var menu = _activeMenu;
        _activeMenu = null;
        if (menu?.IsVisible == true) menu.Close();
    }
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_requestItemId is Guid id && !_host.Items.Any(item => item.Id == id)) CloseActiveMenu();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Items.CollectionChanged -= OnItemsChanged;
        CloseActiveMenu();
    }
    internal static (bool Exists, bool IsDirectory) InspectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, false);
        var directory = Directory.Exists(path);
        return (directory || File.Exists(path), directory);
    }
    internal static DrawerItemContextMenuWindow CreateMenuForPath(string path,
        (bool Exists, bool IsDirectory) state, bool isMappingBox, bool isPixelStyle, int x, int y,
        bool canPaste = false, bool isBusy = false)
        => new(state.Exists && WindowsFileShellActions.CanRunAsAdministrator(path, state.IsDirectory),
            isMappingBox, isPixelStyle, x, y, state.Exists, canPaste,
            isShortcut: WindowsFileShellActions.IsShortcut(path), isBusy: isBusy);
    private string[] ReadClipboardPaths()
    {
        try { return _clipboard.ReadPaths(); }
        catch (System.Runtime.InteropServices.ExternalException) { return []; }
    }
    public async Task InvokeAsync(DrawerItemContextAction action, DrawerItemViewModel? item = null)
    {
        try { await ExecuteAsync(action, item); }
        catch (Exception exception) { ReportFailure(item, exception); }
    }
    private void ReportFailure(DrawerItemViewModel? item, Exception exception)
    {
        if (item is not null) _host.ShowContextMenuFailure(item, exception);
        else
        {
            _host.Logger.Error(exception, "Failed to execute box context action.");
            _host.ReportItemContextAction($"操作失败：{exception.Message}");
        }
    }
    internal async Task ExecuteAsync(DrawerItemContextAction action, DrawerItemViewModel? item)
    {
        if (action == DrawerItemContextAction.None || _disposed) return;
        if (action == DrawerItemContextAction.Paste)
        {
            var paths = _clipboard.ReadPaths();
            if (paths.Length == 0) { _host.ReportItemContextAction("剪贴板没有可粘贴的文件。"); return; }
            await _host.PasteFilePathsAsync(paths);
            return;
        }
        if (item is null) return;
        var current = await _host.GetCurrentFileItemAsync(item);
        var path = current.EffectivePath ?? throw new InvalidOperationException("文件没有可用路径。");
        switch (action)
        {
            case DrawerItemContextAction.Open:
                if (_host.OpenItemCommand.CanExecute(item)) await _host.OpenItemCommand.ExecuteAsync(item);
                break;
            case DrawerItemContextAction.RunAsAdministrator:
                _host.ReportItemContextAction(await WindowsFileShellActions.RunAsAdministratorAsync(path)
                    ? $"已以管理员身份启动 {current.DisplayName}" : "已取消管理员启动");
                break;
            case DrawerItemContextAction.Reveal:
                if (WindowsFileShellActions.IsShortcut(path)) path = await WindowsFileShellActions.GetShortcutTargetAsync(path);
                await WindowsFileShellActions.RevealAsync(path);
                _host.ReportItemContextAction($"已定位 {current.DisplayName}");
                break;
            case DrawerItemContextAction.RevealShortcutLocation:
                await WindowsFileShellActions.RevealAsync(path);
                _host.ReportItemContextAction($"已定位快捷方式：{current.DisplayName}");
                break;
            case DrawerItemContextAction.Copy:
                if (!(await _inspect(path)).Exists) throw new FileNotFoundException("文件不可访问，无法复制。", path);
                _clipboard.CopyFile(path);
                _host.ReportItemContextAction($"已复制 {current.DisplayName}");
                break;
            case DrawerItemContextAction.CopyPath:
                _clipboard.CopyPath(path);
                _host.ReportItemContextAction("已复制文件路径");
                break;
            case DrawerItemContextAction.Rename:
                if (_host.IsBusy) return;
                var dialog = new FileRenameWindow(current.DisplayName, _host.IsMappingBox,
                    current.ItemKind == WitchDrawer.Core.Models.ItemKind.Directory);
                if (dialog.ShowDialog() == true) await _host.RenameFileItemAsync(item, dialog.NewName);
                break;
            case DrawerItemContextAction.RemoveFromBox:
                if (!_host.IsBusy && _host.DeleteItemCommand.CanExecute(item)) await _host.DeleteItemCommand.ExecuteAsync(item);
                break;
        }
    }
}
