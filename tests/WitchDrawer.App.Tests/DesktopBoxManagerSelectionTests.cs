using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定 <see cref="DesktopBoxManager.ShouldClearSelectionOnOutsideClick"/> 的语义：
/// 盒子带 WS_EX_NOACTIVATE，桌面点击不再产生任何 Deactivated 事件，全局鼠标
/// 钩子是唯一的"外部点击"信号；命中点不在本盒子窗口上就必须清掉选中框，
/// 命中本盒子时保留（盒子自己的点击逻辑会接着处理）。
/// </summary>
public sealed class DesktopBoxManagerSelectionTests
{
    [Theory]
    [InlineData(0, 100, true)] // 命中失败/桌面 → 清除
    [InlineData(200, 100, true)] // 其他程序或其他盒子 → 清除
    [InlineData(100, 100, false)] // 本盒子 → 保留
    public void ShouldClearSelectionOnOutsideClick_OnlyKeepsClicksOnOwnWindow(
        int clickedHandle,
        int boxHandle,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopBoxManager.ShouldClearSelectionOnOutsideClick(
                (nint)clickedHandle,
                (nint)boxHandle));
    }
}
