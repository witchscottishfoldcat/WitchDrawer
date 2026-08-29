using System.IO;
using System.Windows;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 监视映射收纳盒已注册的映射源文件夹：内容新增/改名/删除时，
/// 先在后台同步（导入新项），再在 UI 线程触发回调让对应盒子刷新。
/// 事件做了防抖合并，避免连续 IO 风暴。
/// </summary>
public sealed class MappingFolderWatcher : IDisposable
{
    private readonly DrawerService _drawerService;
    private readonly Func<Guid, Task> _onFolderChanged;
    private readonly Dictionary<Guid, List<FileSystemWatcher>> _watchers = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _debounce = [];
    private readonly object _gate = new();
    private bool _disposed;

    public MappingFolderWatcher(DrawerService drawerService, Func<Guid, Task> onFolderChanged)
    {
        _drawerService = drawerService;
        _onFolderChanged = onFolderChanged;
    }

    public async Task SynchronizeAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        var folders = await _drawerService.GetMappingFolderPathsAsync(boxId, cancellationToken);
        Synchronize(boxId, folders);
    }

    public void Synchronize(Guid boxId, IReadOnlyList<string> folderPaths)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_watchers.TryGetValue(boxId, out var existing))
            {
                foreach (var watcher in existing)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }

                _watchers.Remove(boxId);
            }

            var folders = folderPaths
                .Where(path => Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (folders.Count == 0)
            {
                return;
            }

            var list = new List<FileSystemWatcher>(folders.Count);
            foreach (var folder in folders)
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += (_, _) => OnFolderChanged(boxId);
                watcher.Changed += (_, _) => OnFolderChanged(boxId);
                watcher.Renamed += (_, _) => OnFolderChanged(boxId);
                watcher.Deleted += (_, _) => OnFolderChanged(boxId);
                watcher.Error += (_, e) => OnWatcherError(boxId, e);
                list.Add(watcher);
            }

            _watchers[boxId] = list;
        }
    }

    public void StopBox(Guid boxId)
    {
        lock (_gate)
        {
            if (_watchers.TryGetValue(boxId, out var existing))
            {
                foreach (var watcher in existing)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }

                _watchers.Remove(boxId);
            }

            if (_debounce.TryGetValue(boxId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _debounce.Remove(boxId);
            }
        }
    }

    private void OnFolderChanged(Guid boxId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounce.TryGetValue(boxId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cts = new CancellationTokenSource();
            _debounce[boxId] = cts;
        }

        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), token);
                var result = await _drawerService.SyncMappingFoldersAsync(boxId, token);
                if (result.Changed && Application.Current is { } app)
                {
                    await app.Dispatcher.InvokeAsync(() => _onFolderChanged(boxId));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 文件监视的偶发异常不应影响主程序。
            }
        }, token);
    }

    private void OnWatcherError(Guid boxId, ErrorEventArgs e)
    {
        // 缓冲溢出/文件夹被删等：停止该盒监视，避免持续报错风暴。
        StopBox(boxId);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var list in _watchers.Values)
            {
                foreach (var watcher in list)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
            }

            _watchers.Clear();
            foreach (var cts in _debounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _debounce.Clear();
        }
    }
}
