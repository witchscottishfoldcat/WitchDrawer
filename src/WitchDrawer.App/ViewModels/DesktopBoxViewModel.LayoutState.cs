using System.ComponentModel;
using System.Globalization;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的布局状态部分：映射列表宽度、尺寸模式、标题/文件名可见性、卷起与设置联动。</summary>
public sealed partial class DesktopBoxViewModel
{
    public (int Column, int Row) GetListDropSlot(Guid? movingItemId = null)
    {
        var maxRow = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => item.GridRow)
            .DefaultIfEmpty(-1)
            .Max();

        return (0, maxRow + 1);
    }

    public async Task LoadMappingViewModeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var savedMode = await ReadSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"), snapshot);
            SetMappingListMode(string.Equals(savedMode, MappingListViewMode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load mapping view mode.");
        }
    }

    private async Task SetMappingViewModeAsync(bool useListMode)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            SetMappingListMode(useListMode);
            var mode = useListMode ? MappingListViewMode : MappingGridViewMode;
            await _drawerService.SetSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"), mode);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save mapping view mode.");
        }
    }

    private void SetMappingListMode(bool value)
    {
        if (SetProperty(ref _isMappingListMode, value, nameof(IsMappingListMode)))
        {
            if (value && IsFreeSort)
            {
                Items.ReplaceAll(Items
                    .OrderBy(item => item.GridRow)
                    .ThenBy(item => item.GridColumn)
                    .ThenBy(item => item.Model.SortOrder));
            }

            OnPropertyChanged(nameof(IsGridMode));
            OnPropertyChanged(nameof(HeaderRowHeight));
            HideDragPreview();
            UpdateItemIconSizes();
        }
    }

    public void ResizeMappingListWidth(double width)
    {
        if (!IsMappingBox)
        {
            return;
        }

        _hasCustomMappingListWidth = true;
        SetProperty(
            ref _mappingListWidth,
            NormalizeMappingListWidth(width, LayoutSettings.MappingListWidth),
            nameof(MappingListWidth));
    }

    public async Task LoadMappingListWidthAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var saved = await ReadSettingAsync(GetMappingListWidthSettingKey(BoxId), snapshot);
            if (double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && double.IsFinite(width))
            {
                _hasCustomMappingListWidth = true;
                SetProperty(
                    ref _mappingListWidth,
                    NormalizeMappingListWidth(width, LayoutSettings.MappingListWidth),
                    nameof(MappingListWidth));
            }
            else
            {
                _hasCustomMappingListWidth = false;
                SetProperty(
                    ref _mappingListWidth,
                    LayoutSettings.MappingListWidth,
                    nameof(MappingListWidth));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load mapping list width.");
        }
    }

    public async Task SaveMappingListWidthAsync()
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            await _drawerService.SetSettingAsync(
                GetMappingListWidthSettingKey(BoxId),
                MappingListWidth.ToString("R", CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save mapping list width.");
        }
    }

    internal static string GetMappingListWidthSettingKey(Guid boxId) =>
        MappingListWidthSettingPrefix + boxId.ToString("N");

    internal static double NormalizeMappingListWidth(double width, double fallback)
    {
        var candidate = double.IsFinite(width) ? width : fallback;
        return Math.Clamp(candidate, MinimumMappingListWidth, MaximumMappingListWidth);
    }

    private async Task MoveMappingListItemWithinBoxAsync(
        DrawerItemViewModel item,
        int targetIndex)
    {
        var originalOrder = Items.ToList();
        var sourceIndex = originalOrder.FindIndex(candidate => candidate.Id == item.Id);
        if (sourceIndex < 0)
        {
            return;
        }

        var reordered = originalOrder.ToList();
        reordered.RemoveAt(sourceIndex);
        targetIndex = Math.Clamp(targetIndex, 0, reordered.Count);
        reordered.Insert(targetIndex, item);
        if (originalOrder.Select(candidate => candidate.Id)
            .SequenceEqual(reordered.Select(candidate => candidate.Id)))
        {
            return;
        }

        // 列表顺序仍由网格坐标持久化。把当前有序格位依次分配给新列表顺序，
        // 既保持图标布局占用形状不变，也确保重启后列表按相同顺序恢复。
        var orderedSlots = originalOrder
            .Select(candidate => (candidate.GridColumn, candidate.GridRow))
            .ToList();
        var positions = reordered
            .Select((candidate, index) => new
            {
                candidate.Id,
                GridColumn = orderedSlots[index].GridColumn,
                GridRow = orderedSlots[index].GridRow
            })
            .ToDictionary(
                candidate => candidate.Id,
                candidate => (candidate.GridColumn, candidate.GridRow));

        await _drawerService.UpdateItemGridPositionsAsync(positions);
        foreach (var candidate in reordered)
        {
            var position = positions[candidate.Id];
            candidate.SetGridPosition(position.GridColumn, position.GridRow, LayoutSettings);
        }

        Items.Synchronize(reordered);
        UpdateGridCanvasSize();
    }

    /// <summary>
    /// 应用尺寸模式（不触发持久化；持久化由设置页 ViewModel 负责）。
    /// </summary>
    public void ApplySizeMode(BoxSizeModeState state)
    {
        var normalized = SupportsFixedSize && state.IsFixed
            ? new BoxSizeModeState(
                true,
                BoxSizeModeState.ClampColumns(state.Columns),
                BoxSizeModeState.ClampRows(state.Rows))
            : BoxSizeModeState.Adaptive;
        if (_sizeMode == normalized)
        {
            return;
        }

        _sizeMode = normalized;
        OnPropertyChanged(nameof(SizeMode));
        OnPropertyChanged(nameof(IsFixedSize));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        OnPropertyChanged(nameof(FixedCapacity));
        UpdateGridCanvasSize();
    }

    private async Task<string?> ReadSettingAsync(string key, StartupSettingsSnapshot? snapshot)
        => snapshot is not null
            ? snapshot.Get(key)
            : await _drawerService.GetSettingAsync(key);

    internal async Task LoadSizeModeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(BoxViewModel.GetSizeModeSettingKey(BoxId), snapshot);
        ApplySizeMode(BoxSizeModeState.Parse(saved));
    }

    public async Task LoadTitleVisibilityAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(GetTitleVisibilitySettingKey(BoxId), snapshot);
        if (saved is null && IsDrawerBox)
        {
            saved = await ReadSettingAsync(
                GetLegacyDrawerTitleVisibilitySettingKey(BoxId), snapshot);
        }

        ApplyTitleVisibility(!bool.TryParse(saved, out var isVisible) || isVisible);
    }

    public void ApplyTitleVisibility(bool isVisible)
    {
        if (!SetProperty(
                ref _isTitleVisible,
                isVisible,
                nameof(IsTitleVisible)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(IsHeaderTitleVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(DrawerContentHeight));
    }

    public async Task LoadRollUpStateAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(GetRollUpSettingKey(BoxId), snapshot);
        ApplyRollUpState(bool.TryParse(saved, out var isRolledUp) && isRolledUp);
    }

    public async Task LoadHoverRollUpEnabledAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(
            GetHoverRollUpEnabledSettingKey(BoxId), snapshot);
        ApplyHoverRollUpEnabled(bool.TryParse(saved, out var isEnabled) && isEnabled);
    }

    internal void ApplyHoverRollUpEnabled(bool isEnabled)
    {
        SetProperty(
            ref _isHoverRollUpEnabled,
            SupportsRollUp && isEnabled,
            nameof(IsHoverRollUpEnabled));
    }

    internal void ApplyRollUpState(bool isRolledUp)
    {
        if (!SetProperty(ref _isRolledUp, SupportsRollUp && isRolledUp, nameof(IsRolledUp)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeaderTitleVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(ContentRowHeight));
    }

    public async Task SaveRollUpStateAsync()
    {
        try
        {
            await _drawerService.SetSettingAsync(GetRollUpSettingKey(BoxId), IsRolledUp.ToString());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop box roll-up state.");
        }
    }

    public async Task LoadFileNameVisibilityAsync(StartupSettingsSnapshot? snapshot = null)
    {
        var saved = await ReadSettingAsync(GetFileNameVisibilitySettingKey(BoxId), snapshot);
        ApplyFileNameVisibility(bool.TryParse(saved, out var isVisible) && isVisible);
    }

    public void ApplyFileNameVisibility(bool isVisible)
    {
        LayoutSettings.IsFileNameVisible = isVisible;
        SetProperty(
            ref _isFileNameVisible,
            isVisible,
            nameof(IsFileNameVisible));
    }

    internal static string GetTitleVisibilitySettingKey(Guid boxId) =>
        $"{TitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetLegacyDrawerTitleVisibilitySettingKey(Guid boxId) =>
        $"{LegacyDrawerTitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetFileNameVisibilitySettingKey(Guid boxId) =>
        $"{FileNameVisibilitySettingPrefix}{boxId:N}";

    internal static string GetRollUpSettingKey(Guid boxId) =>
        $"{RollUpSettingPrefix}{boxId:N}";

    internal static string GetHoverRollUpEnabledSettingKey(Guid boxId) =>
        $"{HoverRollUpEnabledSettingPrefix}{boxId:N}";

    private void OnLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopBoxLayoutSettings.MappingListWidth)
            && !_hasCustomMappingListWidth)
        {
            SetProperty(
                ref _mappingListWidth,
                LayoutSettings.MappingListWidth,
                nameof(MappingListWidth));
        }

        if (e.PropertyName is nameof(DesktopBoxLayoutSettings.DrawerCoverCellWidth)
            or nameof(DesktopBoxLayoutSettings.DrawerCoverCellHeight)
            or nameof(DesktopBoxLayoutSettings.DrawerCoverCellSize))
        {
            return;
        }

        foreach (var item in Items)
        {
            item.UpdateCanvasPosition(LayoutSettings);
        }

        UpdateItemIconSizes();
        UpdateGridCanvasSize();
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        if (IsDrawerBox
            && e.PropertyName is nameof(DesktopBoxLayoutSettings.CurrentPreset)
                or nameof(DesktopBoxLayoutSettings.IsFileNameVisible))
        {
            ResizeDrawerCover(
                (DrawerCoverColumns * LayoutSettings.DrawerCoverCellWidth)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2),
                (DrawerCoverRows * LayoutSettings.DrawerCoverCellHeight)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2));
            OnPropertyChanged(nameof(DrawerCoverCapacity));
            OnPropertyChanged(nameof(DrawerHasOverflow));
            OnPropertyChanged(nameof(DrawerDirectItemCount));
            OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
            OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
            RefreshDrawerPreview();
        }
    }
}
