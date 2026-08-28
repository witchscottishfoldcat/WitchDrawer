namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 自动隐藏开启后，鼠标悬停到某个收纳盒上时取消隐藏的范围。
/// </summary>
public enum AutoHideRevealScope
{
    /// <summary>仅指向的收纳盒取消隐藏，其余保持隐藏。</summary>
    HoveredBoxOnly = 0,

    /// <summary>悬停任一收纳盒时，全部收纳盒一起取消隐藏。</summary>
    AllBoxes = 1
}