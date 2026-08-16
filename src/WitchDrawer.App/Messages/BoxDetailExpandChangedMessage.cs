namespace WitchDrawer.App.Messages;

/// <summary>
/// 映射盒「详细功能」开关变更消息（按盒）。由主控台盒操作菜单发出，
/// 桌面盒窗口订阅后即时启用/禁用详细展开与拖拽交换能力。
/// </summary>
public sealed record BoxDetailExpandChangedMessage(
    Guid BoxId,
    bool IsEnabled);
