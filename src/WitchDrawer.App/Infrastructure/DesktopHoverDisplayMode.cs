namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 全局“图标名称（悬停提示）”显示模式的实时状态。
/// <see langword="false"/> = 完整显示（文件路径），<see langword="true"/> = 精简显示（文件名，快捷方式自动去掉 .lnk）。
/// 由 MainViewModel 在切换时更新，并在接收消息的桌面收纳盒上触发重绘。
/// </summary>
public static class DesktopHoverDisplayMode
{
    public static bool IsCompact { get; set; }
}