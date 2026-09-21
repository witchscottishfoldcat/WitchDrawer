using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Native.Files;

/// <summary>
/// <see cref="ShellChangeNotifier"/> 的接口适配层：App 层通过 Core 的
/// <see cref="IShellChangeNotifier"/> 抽象调用，避免 ViewModel 直接依赖 Native。
/// </summary>
public sealed class ShellChangeNotifierService : IShellChangeNotifier
{
    public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) =>
        ShellChangeNotifier.NotifyItemImportedAsync(item, logger);

    public void NotifyCreated(string path, bool isDirectory) =>
        ShellChangeNotifier.NotifyCreated(path, isDirectory);

    public void NotifyFolderItemCreated(string itemPath, bool isDirectory) =>
        ShellChangeNotifier.NotifyFolderItemCreated(itemPath, isDirectory);
}
