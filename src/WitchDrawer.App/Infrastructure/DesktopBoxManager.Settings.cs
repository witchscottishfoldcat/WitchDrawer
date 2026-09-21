using WitchDrawer.App.Messages;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    // 设置消息应用与自动隐藏多盒协调。

    private void ApplyBoxLayoutPreset(BoxLayoutPresetChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.ViewModel.LayoutSettings.ApplyPresetWithoutCallback(message.Preset);
    }

    private void ApplyBoxSizeMode(BoxSizeModeChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.ViewModel.ApplySizeMode(
            new BoxSizeModeState(message.IsFixed, message.Columns, message.Rows));
    }

    private void ApplyBoxPositionLockState(
        BoxPositionLockStateChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.SetPositionLocked(message.IsPositionLocked);
        _logger.Info(
            $"Applied position lock state {message.IsPositionLocked} "
            + $"to desktop box {message.BoxId:N}.");
    }

    private void ApplyTitleVisibility(
        BoxTitleVisibilityChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyTitleVisibility(message.IsVisible);
        }
    }

    private void ApplyFileNameVisibility(
        BoxFileNameVisibilityChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ViewModel.ApplyFileNameVisibility(message.IsVisible);
            if (window.ViewModel.IsDrawerBox)
            {
                FireAndForget.Run(
                    window.ViewModel.SaveDrawerCoverSizeAsync(),
                    _logger,
                    $"Failed to save resized drawer cover for box {message.BoxId:N}.");
            }
        }
    }

    private void ApplyHoverRollUpEnabled(
        BoxHoverRollUpEnabledChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window))
        {
            window.ApplyHoverRollUpEnabled(message.IsEnabled);
        }
    }

    private void ApplyDrawerSortMode(DrawerSortModeChangedMessage message)
    {
        if (_windows.TryGetValue(message.BoxId, out var window)
            && window.ViewModel.ApplyDrawerSortMode(message.SortMode))
        {
            // 排序模式变化：重排盒内显示（自由模式则从 DB 恢复记忆布局）。
            _ = window.ViewModel.LoadAsync();
        }
    }

    private void ApplyAutoHideSettings(AutoHideSettingsChangedMessage message)
    {
        _autoHideSettings = new AutoHideSettings(
            message.IsEnabled,
            message.HiddenPercent,
            message.RevealScope,
            message.FadeWholeBox,
            message.FadeTitle,
            message.FadeBorder);
        foreach (var window in _windows.Values)
        {
            window.ApplyAutoHideState(_autoHideSettings);
        }

        // 悬停集合保留：scope/opacity 变更后 Reveal 由悬停态 + scope 即时重算，
        // 清空会让正在交互的盒瞬间收缩隐藏。已移除窗口的残留 boxId 会被
        // ReevaluateAutoHide 忽略（仅遍历当前 _windows）。
        ReevaluateAutoHide();
    }

    /// <summary>
    /// 全局“图标名称（悬停提示）”模式切换后，更新实时状态并刷新所有已显示盒子的提示文本。
    /// </summary>
    private void ApplyIconToolTipMode(IconToolTipModeChangedMessage message)
    {
        DesktopHoverDisplayMode.IsCompact = message.IsCompact;
        foreach (var window in _windows.Values)
        {
            window.ApplyHoverDisplayMode();
        }
    }

    private void OnWindowAutoHideHoverEntered(object? sender, EventArgs e)
    {
        if (sender is DesktopBoxWindow window
            && _autoHideHoveredBoxIds.Add(window.ViewModel.BoxId))
        {
            ReevaluateAutoHide();
        }
    }

    private void OnWindowAutoHideHoverLeft(object? sender, EventArgs e)
    {
        if (sender is DesktopBoxWindow window
            && _autoHideHoveredBoxIds.Remove(window.ViewModel.BoxId))
        {
            ReevaluateAutoHide();
        }
    }

    private void ReevaluateAutoHide()
    {
        var revealStates = ComputeAutoHideReveal(
            _windows.Keys,
            _autoHideSettings,
            _autoHideHoveredBoxIds);
        foreach (var (boxId, window) in _windows)
        {
            window.SetAutoHideReveal(revealStates[boxId]);
        }
    }

    /// <summary>
    /// 纯函数：根据悬停集合与悬停取消隐藏范围，计算每个收纳盒是否取消隐藏。
    /// 未开启自动隐藏时所有盒内容完全可见。
    /// </summary>
    internal static IReadOnlyDictionary<Guid, bool> ComputeAutoHideReveal(
        IEnumerable<Guid> boxIds,
        AutoHideSettings settings,
        IEnumerable<Guid> hoveredBoxIds)
    {
        var result = new Dictionary<Guid, bool>();
        foreach (var boxId in boxIds)
        {
            result[boxId] = true;
        }

        if (!settings.IsEnabled || result.Count == 0)
        {
            return result;
        }

        var hovered = hoveredBoxIds as IReadOnlyCollection<Guid>
            ?? new HashSet<Guid>(hoveredBoxIds);
        var revealAll = settings.RevealScope == AutoHideRevealScope.AllBoxes
            && hovered.Count > 0;
        foreach (var boxId in result.Keys.ToArray())
        {
            var revealed = revealAll
                || (settings.RevealScope == AutoHideRevealScope.HoveredBoxOnly
                    && hovered.Contains(boxId));
            result[boxId] = revealed;
        }

        return result;
    }
}
