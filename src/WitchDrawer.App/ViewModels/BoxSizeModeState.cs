namespace WitchDrawer.App.ViewModels;

/// <summary>
/// 收纳盒尺寸模式：自适应（窗口随内容撑开）或固定 m×n 图标格。
/// 固定模式下窗口尺寸锁定，超出内容在盒内滚动。
/// </summary>
public sealed record BoxSizeModeState(bool IsFixed, int Columns, int Rows)
{
    public const int MinCells = 1;
    // The old 12 x 8 values are viewport limits, not valid fixed-layout limits.
    // Keep only a remote safety ceiling for corrupted/manual settings; ordinary
    // users can keep increasing the fixed grid while the window stays virtualized.
    public const int MaxColumns = 1000;
    public const int MaxRows = 1000;

    public static BoxSizeModeState Adaptive { get; } = new(IsFixed: false, Columns: 4, Rows: 4);

    public string Serialize() => IsFixed ? $"Fixed:{Columns}:{Rows}" : "Adaptive";

    public static BoxSizeModeState Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, "Adaptive", StringComparison.Ordinal))
        {
            return Adaptive;
        }

        var parts = raw.Split(':');
        if (parts.Length == 3
            && string.Equals(parts[0], "Fixed", StringComparison.Ordinal)
            && int.TryParse(parts[1], out var columns)
            && int.TryParse(parts[2], out var rows))
        {
            return new BoxSizeModeState(true, ClampColumns(columns), ClampRows(rows));
        }

        return Adaptive;
    }

    public static int ClampColumns(int columns) => Math.Clamp(columns, MinCells, MaxColumns);

    public static int ClampRows(int rows) => Math.Clamp(rows, MinCells, MaxRows);

    /// <summary>
    /// 固定尺寸不得小于当前内容实际撑开的格子范围。
    /// </summary>
    public bool FitsExtent(int extentColumns, int extentRows) =>
        !IsFixed
        || (Columns >= Math.Max(MinCells, extentColumns)
            && Rows >= Math.Max(MinCells, extentRows));
}
