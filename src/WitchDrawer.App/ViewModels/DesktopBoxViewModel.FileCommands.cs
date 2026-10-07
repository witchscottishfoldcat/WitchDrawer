using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxViewModel
{
    internal Task<DrawerItem> GetCurrentFileItemAsync(DrawerItemViewModel item)
        => _drawerService.GetItemInBoxAsync(BoxId, item.Id);

    internal async Task RenameFileItemAsync(DrawerItemViewModel item, string name)
    {
        if (IsBusy) throw new InvalidOperationException(Strings.Get("TheBoxIsProcessingFilesTryAgainLater"));
        IsBusy = true;
        try
        {
            var result = await _drawerService.RenameItemAsync(BoxId, item.Id, name);
            await LoadAsync();
            StatusText = IsMappingBox ? Strings.Format("RenamedReference", result.DisplayName) : Strings.Format("Renamed", result.DisplayName);
        }
        finally { IsBusy = false; }
    }

    internal async Task PasteFilePathsAsync(string[] paths)
    {
        if (IsBusy) throw new InvalidOperationException(Strings.Get("TheBoxIsProcessingFilesTryAgainLater"));
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
            var operation = IsMappingBox ? Strings.Get("Referenced") : Strings.Get("Copy");
            StatusText = failures.Count == 0
                ? Strings.Format("Items2", operation, succeeded)
                : Strings.Format("ItemsFailed", operation, succeeded, failures.Count, failures[0]);
        }
        finally { IsBusy = false; }
    }
}
