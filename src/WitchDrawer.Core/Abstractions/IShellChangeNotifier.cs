using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Abstractions;

/// <summary>
/// 通知 Explorer 文件系统变化（导入后刷新源目录、导出/新建后刷新目标目录）。
/// </summary>
public interface IShellChangeNotifier
{
    Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger);

    void NotifyCreated(string path, bool isDirectory);

    void NotifyFolderItemCreated(string itemPath, bool isDirectory);
}
