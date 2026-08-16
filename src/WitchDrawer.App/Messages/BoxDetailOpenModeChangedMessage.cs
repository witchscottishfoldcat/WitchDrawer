namespace WitchDrawer.App.Messages;

/// <summary>
/// 映射盒「详细视图打开方式」（单击/双击展开）被切换时广播，
/// 主控台（BoxViewModel）与桌面盒窗口（DesktopBoxViewModel）据此同步。
/// </summary>
public sealed record BoxDetailOpenModeChangedMessage(Guid BoxId, bool IsSingleClick);
