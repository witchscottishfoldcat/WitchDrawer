using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxViewModel
{
    internal Task<DrawerItem> GetCurrentFileItemAsync(DrawerItemViewModel item)
        => _drawerService.GetItemInBoxAsync(BoxId, item.Id);

    internal async Task RenameFileItemAsync(DrawerItemViewModel item, string name)
    {
        if (IsBusy) throw new InvalidOperationException("盒子正在处理文件，请稍后重试。");
        IsBusy = true;
        try
        {
            var result = await _drawerService.RenameItemAsync(BoxId, item.Id, name);
            await LoadAsync();
            StatusText = IsMappingBox ? $"已重命名引用：{result.DisplayName}" : $"已重命名：{result.DisplayName}";
        }
        finally { IsBusy = false; }
    }

    internal async Task PasteFilePathsAsync(string[] paths)
    {
        if (IsBusy) throw new InvalidOperationException("盒子正在处理文件，请稍后重试。");
        if (IsTodoBox || paths.Length == 0) return;
        IsBusy = true;
        var succeeded = 0;
        var failures = new List<string>();
        try
        {
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (IsMappingBox) await _drawerService.ImportPathAsync(BoxId, path);
                    else await _drawerService.CopyPathToBoxAsync(BoxId, path);
                    succeeded++;
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Failed to paste file into box.");
                    failures.Add(exception.Message);
                }
            }
            await LoadAsync();
            var operation = IsMappingBox ? "加入引用" : "复制";
            StatusText = failures.Count == 0
                ? $"已{operation} {succeeded} 项"
                : $"已{operation} {succeeded} 项，{failures.Count} 项失败：{failures[0]}";
        }
        finally { IsBusy = false; }
    }
}
