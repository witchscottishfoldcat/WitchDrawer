using System.Windows;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 自动隐藏：reveal 状态计算与悬停事件上报（多盒协调在 DesktopBoxManager）。
/// </summary>
public partial class DesktopBoxWindow
{
    /// <summary>自动隐藏悬停进入/离开事件，由 DesktopBoxManager 统一协调多盒 reveal。</summary>
    internal event EventHandler? AutoHideHoverEntered;

    internal event EventHandler? AutoHideHoverLeft;

    /// <summary>
    /// 全局“图标名称（悬停提示）”模式切换后，刷新盒内所有条目的悬停提示文本。
    /// </summary>
    internal void ApplyHoverDisplayMode()
    {
        ViewModel.RefreshItemHoverDisplayTexts();
    }

    /// <summary>
    /// 应用自动隐藏的当前配置。未开启时内容、盒子外壳与标题始终完全可见，悬停事件不产生副作用。
    /// </summary>
    internal void ApplyAutoHideState(AutoHideSettings settings)
    {
        _autoHideEnabled = settings.IsEnabled;
        _autoHideHiddenContentOpacity = Math.Clamp(settings.ContentOpacity, 0, 1);
        _autoHideFadeWholeBox = settings.FadeWholeBox;
        _autoHideFadeTitle = settings.FadeTitle;
        _autoHideFadeBorder = settings.FadeBorder;
        SetAutoHideReveal(_autoHideRevealed);
    }

    /// <summary>
    /// 设置该收纳盒是否取消隐藏（悬停命中）。与桌面盒子透明度无关。
    /// 盒子外壳、标题、边框是否一并透明由 ApplyAutoHideState 传入的勾选项决定，
    /// 各自独立计算，不与内容透明叠加。
    /// </summary>
    internal void SetAutoHideReveal(bool revealed)
    {
        if (!_autoHideEnabled)
        {
            revealed = true;
        }

        _autoHideRevealed = revealed;
        var hiddenBoxOpacity = _autoHideFadeWholeBox ? _autoHideHiddenContentOpacity : 1;
        var hiddenTitleOpacity = _autoHideFadeTitle ? _autoHideHiddenContentOpacity : 1;
        var hiddenBorderOpacity = _autoHideFadeBorder ? _autoHideHiddenContentOpacity : 1;
        ViewModel.SetAutoHideReveal(
            revealed,
            _autoHideHiddenContentOpacity,
            hiddenBoxOpacity,
            hiddenTitleOpacity,
            hiddenBorderOpacity);
    }

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        CancelPendingHoverRollUp();
        if (_autoHideEnabled)
        {
            AutoHideHoverEntered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        ScheduleHoverRollUp();
        if (_autoHideEnabled)
        {
            AutoHideHoverLeft?.Invoke(this, EventArgs.Empty);
        }
    }
}
