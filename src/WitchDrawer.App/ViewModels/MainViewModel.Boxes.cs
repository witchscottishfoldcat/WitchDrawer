using System.Threading;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;
using WitchDrawer.Native.Windows;

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
                    _drawerService,
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

            // 必须在首次启动标记写入前判断是否为旧安装，才能让新用户使用二段透明度，
            // 同时让升级用户保留旧主题原本的视觉效果。
            await RestoreThemeBoxOpacitiesAsync(startupSnapshot);
            await RestoreAppearanceOpacitiesAsync(startupSnapshot);
            var editorOpacityFollowSetting =
                await ReadSettingAsync(EditorFollowsBoxOpacitySettingKey, startupSnapshot);
            EditorFollowsBoxOpacity = bool.TryParse(
                editorOpacityFollowSetting,
                out var editorFollowsBoxOpacity)
                && editorFollowsBoxOpacity;

            var iconToolTipCompactSetting =
                await ReadSettingAsync(IconToolTipCompactSettingKey, startupSnapshot);
            IconToolTipCompact = bool.TryParse(
                iconToolTipCompactSetting,
                out var iconToolTipCompact)
                && iconToolTipCompact;
            PublishIconToolTipMode();

            var autoHideSettings = await _autoHideSettingsStore.LoadAsync(startupSnapshot: startupSnapshot);
            AutoHideEnabled = autoHideSettings.IsEnabled;
            AutoHideHiddenTransparencyPercent = autoHideSettings.HiddenTransparencyPercent;
            AutoHideRevealScope = autoHideSettings.RevealScope;
            AutoHideFadeWholeBox = autoHideSettings.FadeWholeBox;
            AutoHideFadeTitle = autoHideSettings.FadeTitle;
            AutoHideFadeBorder = autoHideSettings.FadeBorder;
            PublishAutoHideSettings();

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

            LaunchOnStartup = ReadStartupRegistry();
            AreDesktopIconsHidden = DesktopIconVisibility.IsHidden();
            var desktopDoubleClickSetting =
                await ReadSettingAsync(DesktopDoubleClickSettingKey, startupSnapshot);
            IsDesktopDoubleClickEnabled =
                bool.TryParse(desktopDoubleClickSetting, out var desktopDoubleClickEnabled)
                && desktopDoubleClickEnabled;
            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Reloads items/quick-panel state without raising BoxesChanged (avoids desktop refresh loops).
    /// </summary>
    public async Task ReloadItemsFromDesktopAsync(Guid? affectedBoxId = null)
    {
        if (IsBusy)
        {
            // 忙时合流而非丢弃：记录一次待刷，忙完补刷；null 表示全量，
            // 一旦出现第二个不同盒子的变更就保持全量。
            _pendingDesktopReloadBoxId = _pendingDesktopReload
                ? (_pendingDesktopReloadBoxId == affectedBoxId ? affectedBoxId : null)
                : affectedBoxId;
            _pendingDesktopReload = true;
            return;
        }

        try
        {
            IsBusy = true;
            if (affectedBoxId is null || SelectedBox?.Id == affectedBoxId.Value)
            {
                await LoadItemsForSelectedBoxAsync(SelectedBox);
            }
            if (IsArchivePage)
            {
                await LoadArchivedTodosAsync();
            }
            if (affectedBoxId is Guid boxId)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
            }
            else
            {
                await _quickPanelViewModel.RefreshAllAsync();
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
        var boxId = _pendingDesktopReloadBoxId;
        _pendingDesktopReloadBoxId = null;
        _ = ReloadItemsFromDesktopAsync(boxId);
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
            StatusText = $"已调整“{draggedBox.Name}”的排列位置";
        }
        catch (Exception exception)
        {
            var currentIndex = Boxes.IndexOf(draggedBox);
            if (currentIndex >= 0 && currentIndex != originalIndex)
            {
                Boxes.Move(currentIndex, originalIndex);
            }

            _logger.Error(exception, "Failed to reorder boxes.");
            StatusText = "收纳盒排序保存失败，已恢复原顺序";
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            StatusText = "请先选择一个收纳盒";
            return;
        }

        if (selectedBox.IsTodoBox)
        {
            StatusText = "待办收纳盒请使用任务输入框添加事项";
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
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = importFailure is not null
                ? $"已导入 {imported} 项，其余未导入：{importFailure.Message}"
                : skippedForCapacity > 0
                ? imported > 0
                    ? $"已导入 {imported} 项到 {selectedBox.Name}，盒子已满（{skippedForCapacity} 项未导入）"
                    : $"{selectedBox.Name} 已满，无法导入"
                : $"已导入 {imported} 项到 {selectedBox.Name}";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
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
                BoxType.Normal => "普通收纳盒",
                BoxType.Mapping => "映射收纳盒",
                BoxType.Pixel => "像素收纳盒",
                BoxType.Todo => "待办收纳盒",
                BoxType.Drawer => "抽屉盒",
                _ => "收纳盒"
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
            StatusText = $"已创建 {name}，桌面收纳栏已生成";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
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
            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = $"已将“{selectedBox.Name}”切换为{option.Name}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
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
                ? $"已锁定“{selectedBox.Name}”的桌面位置"
                : $"已解锁“{selectedBox.Name}”的桌面位置";
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
                    _drawerService,
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

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = result.StatusMessage;
            BoxesChanged?.Invoke(this, EventArgs.Empty);
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
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

            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                var boxViewModel = new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger);
                await boxViewModel.InitializeSettingsAsync();
                Boxes.Add(boxViewModel);
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(b => b.Id == selectedBox.Id) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = $"已重命名收纳盒为 {newName.Trim()}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
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
            StatusText = $"已打开 {item.DisplayName}";
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
            await _quickPanelViewModel.RefreshBoxAsync(item.Model.BoxId);
            StatusText = result.StatusMessage;
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(item.Model.BoxId));
        });
    }
}
