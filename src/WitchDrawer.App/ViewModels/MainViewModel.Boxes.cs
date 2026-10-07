using WitchDrawer.Core.Localization;
using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

// 盒子集合与增删改查、条目加载管道及条目操作。
public sealed partial class MainViewModel
{
    public async Task LoadAsync(StartupSettingsSnapshot? startupSnapshot = null)
    {
        await RunBusyAsync(async () =>
        {
            var existingSelection = SelectedBox?.Id;
            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes, startupSnapshot);

            var loadedBoxes = new List<BoxViewModel>(presentedBoxes.Length);
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                var boxViewModel = new BoxViewModel(
                    box,
                    _drawerService.Settings,
                    visualStyle,
                    isPositionLocked,
                    _logger);
                await boxViewModel.InitializeSettingsAsync(startupSnapshot);
                loadedBoxes.Add(boxViewModel);
            }
            // The sidebar is already attached to Boxes. Publish the startup set once
            // so WPF does not remeasure it after every individual Add.
            Boxes.ReplaceAll(loadedBoxes);

            await SelectBoxAsync(Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault());

            await Settings.LoadAsync(startupSnapshot);

            var aboutPageShown = await ReadSettingAsync(AboutPageShownSettingKey, startupSnapshot);
            if (!bool.TryParse(aboutPageShown, out var hasShownAboutPage) || !hasShownAboutPage)
            {
                ShowAboutCommand.Execute(null);
                try
                {
                    await _drawerService.SetSettingAsync(AboutPageShownSettingKey, bool.TrueString);
                }
                catch (Exception exception)
                {
                    // The guide is still useful if the preference cannot be persisted;
                    // try again on the next launch instead of failing startup.
                    _logger.Error(exception, "Failed to persist first-launch about-page preference.");
                }
            }

            StatusText = Strings.Format("SyncedBoxesToTheDesktop", Boxes.Count);
            BoxesChanged?.Invoke(this, new());
        });
    }

    /// <summary>
    /// Reloads this window after committed content changes without notifying other surfaces.
    /// </summary>
    public async Task RefreshContentAsync(BoxRefreshRequest request)
    {
        // Presentation commands have already updated this window locally.
        if (request.PresentationOnly) return;

        if (IsBusy)
        {
            // 忙时合流而非丢弃：记录一次待刷，忙完补刷；BoxIds 为 null 表示全量，
            // 对同一批次受影响盒子合并，避免刷新无关盒子。
            _pendingDesktopReloadRequest = _pendingDesktopReloadRequest is null
                ? request : _pendingDesktopReloadRequest.Merge(request);
            _pendingDesktopReload = true;
            return;
        }

        try
        {
            IsBusy = true;
            if (SelectedBox is null || request.Affects(SelectedBox.Id))
            {
                await LoadItemsForSelectedBoxAsync(SelectedBox);
            }
            if (IsArchivePage)
            {
                await Archive.LoadAsync();
            }

        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reload items from desktop boxes.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            FlushPendingDesktopReload();
        }
    }

    private void FlushPendingDesktopReload()
    {
        if (!_pendingDesktopReload || IsBusy)
        {
            return;
        }

        _pendingDesktopReload = false;
        var request = _pendingDesktopReloadRequest!;
        _pendingDesktopReloadRequest = null;
        _ = RefreshContentAsync(request);
    }

    public async Task ReorderBoxAsync(Guid draggedBoxId, Guid targetBoxId, bool insertAfter)
    {
        var draggedBox = Boxes.FirstOrDefault(box => box.Id == draggedBoxId);
        var targetBox = Boxes.FirstOrDefault(box => box.Id == targetBoxId);
        if (draggedBox is null || targetBox is null || ReferenceEquals(draggedBox, targetBox))
        {
            return;
        }

        var originalIndex = Boxes.IndexOf(draggedBox);
        var originalOrder = Boxes.Select(box => box.Id).ToArray();
        Boxes.RemoveAt(originalIndex);

        var targetIndex = Boxes.IndexOf(targetBox);
        var insertionIndex = insertAfter ? targetIndex + 1 : targetIndex;
        Boxes.Insert(insertionIndex, draggedBox);

        var reorderedIds = Boxes.Select(box => box.Id).ToArray();
        if (reorderedIds.SequenceEqual(originalOrder))
        {
            return;
        }

        try
        {
            await _drawerService.ReorderBoxesAsync(reorderedIds);
            SelectedBox = draggedBox;
            StatusText = Strings.Format("Reordered", draggedBox.Name);
        }
        catch (Exception exception)
        {
            var currentIndex = Boxes.IndexOf(draggedBox);
            if (currentIndex >= 0 && currentIndex != originalIndex)
            {
                Boxes.Move(currentIndex, originalIndex);
            }

            _logger.Error(exception, "Failed to reorder boxes.");
            StatusText = Strings.Get("CouldNotSaveBoxOrderRestoredThePreviousOrder");
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            StatusText = Strings.Get("SelectABoxFirst");
            return;
        }

        if (selectedBox.IsTodoBox)
        {
            StatusText = Strings.Get("UseTheTaskInputFieldToAddTasksTo");
            return;
        }

        var pathList = paths.ToArray();
        if (pathList.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            // 固定格数盒的硬约束：主窗口导入同样不得超容量（桌面盒拖入路径已有同等约束）。
            // 超容量的项目若入库，会因无格位被分配到固定边界外，在盒内不可见也不可选。
            var pathsToImport = pathList;
            var skippedForCapacity = 0;
            if (selectedBox.SupportsFixedSize && BoxSizeSettings.IsFixedMode)
            {
                var capacity = BoxSizeSettings.FixedColumns * BoxSizeSettings.FixedRows;
                var remaining = Math.Max(0, capacity - Items.Count);
                if (remaining < pathList.Length)
                {
                    pathsToImport = pathList.Take(remaining).ToArray();
                    skippedForCapacity = pathList.Length - pathsToImport.Length;
                }
            }

            var imported = 0;
            Exception? importFailure = null;
            try
            {
                using var batch = _drawerService.Changes.BeginBatch();
                foreach (var path in pathsToImport)
                {
                    var importedItem = await _drawerService.ImportPathAsync(selectedBox.Id, path);
                    imported++;
                    await _shellChangeNotifier.NotifyItemImportedAsync(importedItem, _logger);
                }
            }
            catch (Exception exception)
            {
                importFailure = exception;
                _logger.Error(exception, "File import partially failed.");
            }

            await LoadItemsForSelectedBoxAsync(selectedBox);
            StatusText = importFailure is not null
                ? Strings.Format("ImportedItemsRemainingFilesWereNotImported", imported, importFailure.Message)
                : skippedForCapacity > 0
                ? imported > 0
                    ? Strings.Format("ImportedItemsIntoBoxIsFullItemsNotImported", imported, selectedBox.Name, skippedForCapacity)
                    : Strings.Format("IsFullCannotImportFiles", selectedBox.Name)
                : Strings.Format("ImportedItemsInto", imported, selectedBox.Name);
        });
    }

    private Task CreateStyledNormalBoxAsync(BoxVisualStyleOption? option)
    {
        return option is null
            ? Task.CompletedTask
            : CreateBoxAsync(BoxType.Normal, option.Style);
    }

    private async Task CreateBoxAsync(
        BoxType type,
        BoxVisualStyle? visualStyle = null)
    {
        await RunBusyAsync(async () =>
        {
            var prefix = type switch
            {
                BoxType.Normal => Strings.Get("NormalBox"),
                BoxType.Mapping => Strings.Get("MappingBox"),
                BoxType.Pixel => Strings.Get("PixelBox"),
                BoxType.Todo => Strings.Get("TaskBox"),
                BoxType.Drawer => Strings.Get("DrawerBox"),
                _ => Strings.Get("Boxes")
            };
            var matchingBoxCount = type == BoxType.Normal
                ? Boxes.Count(box => box.Type is BoxType.Normal or BoxType.Pixel)
                : Boxes.Count(box => box.Type == type);
            var name = $"{prefix} {matchingBoxCount + 1}";
            var box = await _drawerService.CreateBoxAsync(name, type);
            if (type == BoxType.Drawer)
            {
                await _drawerService.SetSettingAsync(
                    BoxViewModel.GetLayoutPresetSettingKey(box.Id),
                    DesktopBoxLayoutSettings.DefaultDrawerPreset);
            }
            var effectiveStyle = visualStyle ?? BoxVisualStyle.Modern;
            if (type == BoxType.Normal)
            {
                try
                {
                    await _boxVisualStyleStore.SaveAsync(box.Id, effectiveStyle);
                }
                catch
                {
                    await CompensateFailedStyledBoxCreationAsync(box.Id);
                    throw;
                }
            }

            var viewModel = new BoxViewModel(
                box,
                _drawerService,
                effectiveStyle,
                isPositionLocked: false,
                logger: _logger);
            await viewModel.InitializeSettingsAsync();
            Boxes.Add(viewModel);
            await SelectBoxAsync(viewModel);
            StatusText = Strings.Format("CreatedAndItsDesktopBox", name);
            BoxesChanged?.Invoke(this, new(box.Id));
        });
    }

    private async Task SetSelectedBoxVisualStyleAsync(BoxVisualStyleOption? option)
    {
        var selectedBox = SelectedBox;
        if (option is null || selectedBox?.CanSelectVisualStyle != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _boxVisualStyleStore.SaveAsync(selectedBox.Id, option.Style);
            selectedBox.ApplyVisualStyle(option.Style);
            foreach (var item in Items.Where(item => item.Model.BoxId == selectedBox.Id))
                item.UpdateBoxPresentation(selectedBox.Name, option.Style == BoxVisualStyle.Pixel,
                    GetIconPixelSize(option.Style == BoxVisualStyle.Pixel));
            StatusText = Strings.Format("ChangedTo", selectedBox.Name, option.Name);
            BoxesChanged?.Invoke(this, new(selectedBox.Id, presentationOnly: true));
        });
    }

    private async Task ToggleSelectedBoxPositionLockAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var isPositionLocked = !selectedBox.IsPositionLocked;
            await _boxPositionLockStateStore.SaveAsync(
                selectedBox.Id,
                isPositionLocked);
            selectedBox.ApplyPositionLockState(isPositionLocked);
            WeakReferenceMessenger.Default.Send(
                new BoxPositionLockStateChangedMessage(
                    selectedBox.Id,
                    isPositionLocked));
            StatusText = isPositionLocked
                ? Strings.Format("LockedTheDesktopPositionOf", selectedBox.Name)
                : Strings.Format("UnlockedTheDesktopPositionOf", selectedBox.Name);
        });
    }

    private async Task CompensateFailedStyledBoxCreationAsync(Guid boxId)
    {
        try
        {
            await _drawerService.DeleteBoxAsync(boxId);
            _logger.Info(
                $"Removed empty box {boxId:N} after visual style persistence failed.");
        }
        catch (Exception compensationException)
        {
            _logger.Error(
                compensationException,
                $"Failed to remove empty box {boxId:N} after visual style persistence failed.");
        }
    }

    private async Task<string?> ReadSettingAsync(string key, StartupSettingsSnapshot? startupSnapshot)
        => startupSnapshot is not null
            ? startupSnapshot.Get(key)
            : await _drawerService.GetSettingAsync(key);

    private async Task<(Box Box, BoxVisualStyle VisualStyle, bool IsPositionLocked)[]> LoadBoxPresentationAsync(
        IReadOnlyList<Box> boxes,
        StartupSettingsSnapshot? startupSnapshot = null)
    {
        return await Task.WhenAll(
            boxes.Select(async box =>
            {
                var visualStyleTask = _boxVisualStyleStore.LoadAsync(box, startupSnapshot: startupSnapshot);
                var positionLockStateTask =
                    _boxPositionLockStateStore.LoadAsync(box.Id, startupSnapshot: startupSnapshot);
                await Task.WhenAll(visualStyleTask, positionLockStateTask);
                return (
                    Box: box,
                    VisualStyle: await visualStyleTask,
                    IsPositionLocked: await positionLockStateTask);
            }));
    }

    private async Task DeleteSelectedBoxAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _drawerService.DeleteBoxAsync(selectedBox.Id);

            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                var boxViewModel = new BoxViewModel(
                    box,
                    _drawerService.Settings,
                    visualStyle,
                    isPositionLocked,
                    _logger);
                await boxViewModel.InitializeSettingsAsync();
                Boxes.Add(boxViewModel);
            }

            await SelectBoxAsync(
                result.BoxRemoved
                    ? Boxes.FirstOrDefault()
                    : Boxes.FirstOrDefault(box => box.Id == result.BoxId) ?? Boxes.FirstOrDefault());

            StatusText = result.StatusMessage;
            BoxesChanged?.Invoke(this, new(selectedBox.Id));
        });
    }

    private async Task RenameSelectedBoxAsync(string? newName)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null || newName is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.RenameBoxAsync(selectedBox.Id, newName);
            selectedBox.ApplyName(newName.Trim());
            foreach (var item in Items.Where(item => item.Model.BoxId == selectedBox.Id))
                item.UpdateBoxPresentation(selectedBox.Name, item.IsPixelated,
                    GetIconPixelSize(item.IsPixelated));

            StatusText = Strings.Format("RenamedBoxTo", newName.Trim());
            BoxesChanged?.Invoke(this, new(selectedBox.Id, presentationOnly: true));
        });
    }

    private bool UpdateSelectedBoxCore(BoxViewModel? value)
    {
        if (EqualityComparer<BoxViewModel?>.Default.Equals(_selectedBox, value))
        {
            return false;
        }

        _selectedBox = value;
        BoxSizeSettings.SetTargetBox(value);
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(IsSelectedTodoBox));
        OnPropertyChanged(nameof(CanImportFiles));
        DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
        RenameSelectedBoxCommand.NotifyCanExecuteChanged();
        SetSelectedBoxVisualStyleCommand.NotifyCanExecuteChanged();
        ToggleSelectedBoxPositionLockCommand.NotifyCanExecuteChanged();
        return true;
    }

    private async Task SelectBoxAsync(BoxViewModel? box)
    {
        UpdateSelectedBoxCore(box);
        await BoxSizeSettings.WaitForPendingLoadsAsync();
        await LoadItemsForSelectedBoxAsync(box);
    }

    private void QueueSelectedBoxItemsLoad()
    {
        var selectedBox = SelectedBox;
        var (version, cancellationToken) = BeginItemsLoad();
        _ = LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private async Task LoadItemsForSelectedBoxAsync(BoxViewModel? selectedBox)
    {
        var (version, cancellationToken) = BeginItemsLoad();
        await LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private (int Version, CancellationToken CancellationToken) BeginItemsLoad()
    {
        _itemsLoadCts?.Cancel();
        _itemsLoadCts = new CancellationTokenSource();

        var version = Interlocked.Increment(ref _itemsLoadVersion);
        return (version, _itemsLoadCts.Token);
    }

    private bool IsCurrentItemsLoad(BoxViewModel? selectedBox, int version)
    {
        return version == Volatile.Read(ref _itemsLoadVersion)
            && SelectedBox?.Id == selectedBox?.Id;
    }

    private async Task LoadItemsForSelectedBoxAsync(
        BoxViewModel? selectedBox,
        int version,
        CancellationToken cancellationToken)
    {
        if (selectedBox?.IsTodoBox == true)
        {
            if (IsCurrentItemsLoad(selectedBox, version))
            {
                Items.ReplaceAll([]);
            }

            await TodoBoxDetail.LoadAsync(selectedBox.Id);
            return;
        }

        await TodoBoxDetail.LoadAsync(null);
        if (selectedBox is null)
        {
            if (IsCurrentItemsLoad(null, version))
            {
                Items.ReplaceAll([]);
            }

            return;
        }

        try
        {
            var items = await _drawerService.GetItemsAsync(selectedBox.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            var isPixelated = selectedBox.IsPixelStyle;
            Items.ReplaceAll(items.Select(item =>
                new DrawerItemViewModel(
                    item,
                    selectedBox.Name,
                    isPixelated,
                    GetIconPixelSize(isPixelated),
                    _logger)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            _logger.Error(exception, "Failed to load drawer items.");
            StatusText = exception.Message;
        }
    }

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = Strings.Format("Opened", item.DisplayName);
        });
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _drawerService.DeleteItemAsync(item.Id);
            await LoadItemsForSelectedBoxAsync(SelectedBox);
            StatusText = result.StatusMessage;
        });
    }
}
