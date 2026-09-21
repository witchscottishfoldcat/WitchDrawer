using System.Globalization;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的抽屉封面部分：封面尺寸设置、封面预览与二级弹窗容量/尺寸计算。</summary>
public sealed partial class DesktopBoxViewModel
{
    public void ResizeDrawerCover(double width, double height)
    {
        var normalized = NormalizeDrawerCoverSize(
            width,
            height,
            LayoutSettings.DrawerCoverCellWidth,
            LayoutSettings.DrawerCoverCellHeight);
        var widthChanged = SetProperty(
            ref _drawerCoverWidth,
            normalized.Width,
            nameof(DrawerCoverWidth));
        var heightChanged = SetProperty(
            ref _drawerCoverHeight,
            normalized.Height,
            nameof(DrawerCoverHeight));
        var columnsChanged = SetProperty(
            ref _drawerCoverColumns,
            normalized.Columns,
            nameof(DrawerCoverColumns));
        var rowsChanged = SetProperty(
            ref _drawerCoverRows,
            normalized.Rows,
            nameof(DrawerCoverRows));
        if (!widthChanged && !heightChanged && !columnsChanged && !rowsChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(DrawerCoverCapacity));
        OnPropertyChanged(nameof(DrawerHasOverflow));
        OnPropertyChanged(nameof(DrawerDirectItemCount));
        OnPropertyChanged(nameof(DrawerContentHeight));
        RefreshDrawerPreview();
    }

    public async Task LoadDrawerCoverSizeAsync(StartupSettingsSnapshot? snapshot = null)
    {
        if (!IsDrawerBox)
        {
            return;
        }

        var saved = await ReadSettingAsync(GetDrawerCoverSizeSettingKey(BoxId), snapshot);
        if (TryParseDrawerCoverSize(saved, out var width, out var height))
        {
            ResizeDrawerCover(width, height);
            return;
        }

        ResizeDrawerCover(DefaultDrawerCoverWidth, DefaultDrawerCoverHeight);
    }

    public Task SaveDrawerCoverSizeAsync()
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{DrawerCoverWidth:0.##},{DrawerCoverHeight:0.##}");
        return _drawerService.SetSettingAsync(GetDrawerCoverSizeSettingKey(BoxId), value);
    }

    internal static string GetDrawerCoverSizeSettingKey(Guid boxId) =>
        $"{DrawerCoverSizeSettingPrefix}{boxId:N}";

    internal static bool TryParseDrawerCoverSize(
        string? value,
        out double width,
        out double height)
    {
        width = 0;
        height = 0;
        var parts = value?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            && double.IsFinite(width)
            && double.IsFinite(height)
            && width > 0
            && height > 0;
    }

    internal static (double Width, double Height, int Columns, int Rows) NormalizeDrawerCoverSize(
        double width,
        double height,
        double cellSize) => NormalizeDrawerCoverSize(width, height, cellSize, cellSize);

    internal static (double Width, double Height, int Columns, int Rows) NormalizeDrawerCoverSize(
        double width,
        double height,
        double cellWidth,
        double cellHeight)
    {
        var normalizedCellWidth = Math.Clamp(cellWidth, 24, 120);
        var normalizedCellHeight = Math.Clamp(cellHeight, 24, 136);
        const double surfaceInsets = DesktopBoxLayoutSettings.DrawerSurfaceInset * 2;
        var requestedWidth = double.IsFinite(width) ? width : DefaultDrawerCoverWidth;
        var requestedHeight = double.IsFinite(height) ? height : DefaultDrawerCoverHeight;
        var maximumColumns = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellWidth));
        var maximumRows = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellHeight));
        var columns = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedWidth - surfaceInsets) / normalizedCellWidth,
                MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        var rows = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedHeight - surfaceInsets) / normalizedCellHeight,
                MidpointRounding.AwayFromZero),
            1,
            maximumRows);
        if (columns * rows < 2 || (columns == 1 && rows == 2))
        {
            // The minimum drawer is always the established horizontal "1 + four previews"
            // shape. A 1x2 cover makes the primary and composite tiles stack vertically and
            // visually turns the already-finished drawer into a different component.
            columns = 2;
            rows = 1;
        }

        return (
            Math.Round((columns * normalizedCellWidth) + surfaceInsets, 1),
            Math.Round((rows * normalizedCellHeight) + surfaceInsets, 1),
            columns,
            rows);
    }

    internal static int CalculateDrawerDirectItemCount(int itemCount, int capacity)
    {
        var normalizedItemCount = Math.Max(0, itemCount);
        var normalizedCapacity = Math.Max(2, capacity);
        return normalizedItemCount > normalizedCapacity
            ? normalizedCapacity - 1
            : Math.Min(normalizedItemCount, normalizedCapacity);
    }

    internal static double CalculateDrawerContentHeight(
        double coverHeight,
        bool isTitleVisible) => Math.Max(
            1,
            coverHeight - (isTitleVisible ? DrawerTitleHeightCompensation : 0));

    /// <summary>
    /// 展开抽屉二级弹窗前调用：弹窗只展示外层封面装不下的溢出项（顺序与盒内显示顺序
    /// Items，已按排序模式排好保持一致），避免封面已显示的图标在弹窗里重复出现。
    /// </summary>
    public void SyncDrawerSecondaryFromItems()
    {
        // 封面已占据前 DrawerDirectItemCount 个位置；弹窗只承接其后的溢出项。
        // DrawerDirectItemCount 在有溢出时为 封面容量-1（留一格给展开按钮），所以这里的
        // Skip 结果必非空；无溢出时根本没有展开按钮，不会走到这里。
        var overflowItems = Items.Skip(DrawerDirectItemCount).ToArray();
        DrawerSecondaryItems.Synchronize(overflowItems);

        OnPropertyChanged(nameof(DrawerSecondaryColumns));
        OnPropertyChanged(nameof(DrawerSecondaryRows));
        OnPropertyChanged(nameof(DrawerSecondaryHasScrollableOverflow));
        OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
        OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
    }

    internal static int CalculateDrawerSecondaryColumns(int itemCount) => Math.Clamp(
        (int)Math.Ceiling(Math.Sqrt(Math.Max(1, itemCount))),
        2,
        5);

    internal static int CalculateDrawerSecondaryRows(int itemCount, int columns) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(1, itemCount) / (double)Math.Max(1, columns)));

    internal static bool ShouldScrollDrawerSecondary(int rows, double cellHeight) =>
        Math.Max(1, rows) * Math.Max(1, cellHeight)
        > MaximumDrawerSecondaryPanelDimension - DrawerSecondaryPanelChrome;

    private void RefreshDrawerPreview()
    {
        DrawerCoverTiles.Clear();
        DrawerPreviewItems.Clear();
        var directItemCount = DrawerDirectItemCount;
        for (var index = 0; index < directItemCount; index++)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.ForItem(Items[index]));
        }

        if (DrawerHasOverflow)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.Expand());
            foreach (var item in Items.Skip(directItemCount).Take(4))
            {
                DrawerPreviewItems.Add(item);
            }
        }
    }
}
