using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 卷起态悬停展开/收回的计时器状态机（120ms 展开延迟、300ms 收回延迟）。
/// </summary>
public partial class DesktopBoxWindow
{
    internal void ApplyHoverRollUpEnabled(bool isEnabled)
    {
        ViewModel.ApplyHoverRollUpEnabled(isEnabled);
        if (isEnabled)
        {
            return;
        }

        CancelHoverRollUpTimers();
        if (!_isHoverExpandedFromRollUp)
        {
            return;
        }

        _isHoverExpandedFromRollUp = false;
        if (_isRollTransitioning)
        {
            _restoreRolledUpAfterTransition = true;
            return;
        }

        FireAndForget.Run(
            RestoreHoverExpandedBoxAsync(),
            ViewModel.Logger,
            $"Failed to restore rolled-up box {ViewModel.BoxId:N} after disabling hover expansion.");
    }

    private async Task RestoreHoverExpandedBoxAsync()
    {
        while (_isMappingViewTransitioning && !Dispatcher.HasShutdownStarted)
        {
            await Task.Delay(50);
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            await TransitionRollUpStateAsync(rollUp: true, persist: false);
        }
    }

    private void OnRollUpHeaderMouseEnter(object sender, MouseEventArgs e)
    {
        RequestHoverExpand();
    }

    private void RequestHoverExpand()
    {
        CancelPendingHoverRollUp();
        if (_hoverExpandCts is not null
            || _isHoverExpandedFromRollUp
            || _isRollTransitioning
            || _isMappingViewTransitioning
            || !ViewModel.IsHoverRollUpEnabled
            || !ViewModel.IsRolledUp
            || !ViewModel.SupportsRollUp)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        ReplaceCancellationTokenSource(ref _hoverExpandCts, cts);
        FireAndForget.Run(
            ExpandRolledUpBoxAfterHoverAsync(cts),
            ViewModel.Logger,
            $"Failed to expand rolled-up box {ViewModel.BoxId:N} on hover.");
    }

    private async Task ExpandRolledUpBoxAfterHoverAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(HoverExpandDelayMs, cts.Token);
            if (cts.IsCancellationRequested
                || (!RollUpHeader.IsMouseOver && !ViewModel.IsDragOver)
                || !ViewModel.IsRolledUp)
            {
                return;
            }

            _isHoverExpandedFromRollUp = true;
            if (!await TransitionRollUpStateAsync(rollUp: false, persist: false))
            {
                _isHoverExpandedFromRollUp = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ClearCancellationTokenSource(ref _hoverExpandCts, cts);
        }
    }

    private void ScheduleHoverRollUp()
    {
        CancelPendingHoverExpand();
        if (!_isHoverExpandedFromRollUp)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        ReplaceCancellationTokenSource(ref _hoverRollUpCts, cts);
        FireAndForget.Run(
            RollUpHoverExpandedBoxAfterLeaveAsync(cts),
            ViewModel.Logger,
            $"Failed to roll up hover-expanded box {ViewModel.BoxId:N}.");
    }

    private async Task RollUpHoverExpandedBoxAfterLeaveAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(HoverRollUpDelayMs, cts.Token);
            while (!cts.IsCancellationRequested
                   && (_itemContextMenu.IsMenuActive
                       || _itemDragGate.IsEntered
                       || _isSurfaceDragging
                       || Mouse.Captured is Thumb))
            {
                await Task.Delay(100, cts.Token);
            }

            if (cts.IsCancellationRequested
                || IsMouseOver
                || IsCursorOverOpenDrawerPopup()
                || !_isHoverExpandedFromRollUp)
            {
                return;
            }

            _isHoverExpandedFromRollUp = false;
            if (!await TransitionRollUpStateAsync(rollUp: true, persist: false))
            {
                _isHoverExpandedFromRollUp = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ClearCancellationTokenSource(ref _hoverRollUpCts, cts);
        }
    }

    private void CancelPendingHoverExpand() =>
        CancelCancellationTokenSource(ref _hoverExpandCts);

    private void CancelPendingHoverRollUp() =>
        CancelCancellationTokenSource(ref _hoverRollUpCts);

    private void CancelHoverRollUpTimers()
    {
        CancelPendingHoverExpand();
        CancelPendingHoverRollUp();
    }

    private static void ReplaceCancellationTokenSource(
        ref CancellationTokenSource? field,
        CancellationTokenSource replacement)
    {
        var previous = Interlocked.Exchange(ref field, replacement);
        previous?.Cancel();
        previous?.Dispose();
    }

    private static void CancelCancellationTokenSource(ref CancellationTokenSource? field)
    {
        var cts = Interlocked.Exchange(ref field, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private static void ClearCancellationTokenSource(
        ref CancellationTokenSource? field,
        CancellationTokenSource completed)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref field, null, completed), completed))
        {
            completed.Dispose();
        }
    }
}
