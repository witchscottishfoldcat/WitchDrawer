namespace WitchDrawer.App.ViewModels;

/// <summary>DesktopBoxViewModel 的自动隐藏部分：由窗口层驱动的 reveal 可见度写入。</summary>
public sealed partial class DesktopBoxViewModel
{
    /// <summary>
    /// 由窗口层根据自动隐藏状态设置内容、盒子外壳、标题与边框四者的可见度。
    /// </summary>
    /// <param name="revealed">是否取消隐藏（悬停命中时 <see langword="true"/>）。</param>
    /// <param name="hiddenContentOpacity">隐藏时内容的可见度（0..1）。</param>
    /// <param name="hiddenBoxOpacity">隐藏时盒子外壳的可见度（0..1）；未勾选参与透明时传 1。</param>
    /// <param name="hiddenTitleOpacity">隐藏时标题的可见度（0..1）；未勾选参与透明时传 1。</param>
    /// <param name="hiddenBorderOpacity">隐藏时边框的可见度（0..1）；未勾选参与透明时传 1。</param>
    public void SetAutoHideReveal(
        bool revealed,
        double hiddenContentOpacity,
        double hiddenBoxOpacity,
        double hiddenTitleOpacity,
        double hiddenBorderOpacity)
    {
        AutoHideContentOpacity = revealed ? 1 : Math.Clamp(hiddenContentOpacity, 0, 1);
        AutoHideBoxOpacity = revealed ? 1 : Math.Clamp(hiddenBoxOpacity, 0, 1);
        AutoHideTitleOpacity = revealed ? 1 : Math.Clamp(hiddenTitleOpacity, 0, 1);
        AutoHideBorderOpacity = revealed ? 1 : Math.Clamp(hiddenBorderOpacity, 0, 1);
    }
}
