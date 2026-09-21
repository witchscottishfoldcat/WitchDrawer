using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WitchDrawer.App.Controls;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Views;

/// <summary>
/// 卷起/展开状态转换与映射盒网格/列表视图切换的两条互斥动画流水线。
/// </summary>
public partial class DesktopBoxWindow
{
    private async void OnToggleRollUpClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isRollTransitioning || _isMappingViewTransitioning || !ViewModel.SupportsRollUp)
        {
            return;
        }

        CancelHoverRollUpTimers();
        _isHoverExpandedFromRollUp = false;
        await TransitionRollUpStateAsync(!ViewModel.IsRolledUp, persist: true);
    }

    private async Task<bool> TransitionRollUpStateAsync(bool rollUp, bool persist)
    {
        if (_isRollTransitioning || _isMappingViewTransitioning || !ViewModel.SupportsRollUp)
        {
            return false;
        }

        _isRollTransitioning = true;
        var fileListScrollBarVisibility =
            ScrollViewer.GetVerticalScrollBarVisibility(FileList);
        try
        {
            var startWidth = ActualWidth;
            var startHeight = ActualHeight;
            SizeToContent = SizeToContent.Manual;
            MinHeight = 0;
            Width = startWidth;
            Height = startHeight;

            double targetHeight;
            if (rollUp)
            {
                // Measuring by briefly applying IsRolledUp=true exposed the zero-height
                // content row to WPF's render queue for one frame, producing a flash before
                // the real size animation began. The rolled-up height is deterministic, so
                // calculate it without mutating the visible layout.
                targetHeight = CalculateRolledUpWindowHeight(
                    WindowBorder.Margin,
                    WindowBorder.BorderThickness,
                    DesktopBoxViewModel.VisibleHeaderRowHeight);
            }
            else
            {
                // Expansion is safe to measure in-place: the HWND is still pinned to the
                // collapsed height, so the newly measured content remains clipped until the
                // height animation reveals it.
                ViewModel.ApplyRollUpState(false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
                WindowBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                targetHeight = WindowBorder.DesiredSize.Height;
            }

            // Mapping list mode normally owns an Auto scrollbar. Constraining the HWND during
            // the transition can make it appear for a single frame, so suppress it until the
            // final layout has stabilized.
            ScrollViewer.SetVerticalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
            await AnimateWindowSizeAsync(startWidth, startHeight, startWidth, targetHeight);
            ViewModel.ApplyRollUpState(rollUp);
            await StabilizeRollUpLayoutAsync();
            if (persist)
            {
                await ViewModel.SaveRollUpStateAsync();
            }

            return true;
        }
        finally
        {
            var restoreRolledUp = _restoreRolledUpAfterTransition;
            _restoreRolledUpAfterTransition = false;
            if (restoreRolledUp)
            {
                ViewModel.ApplyRollUpState(true);
                await StabilizeRollUpLayoutAsync();
            }

            ScrollViewer.SetVerticalScrollBarVisibility(
                FileList,
                fileListScrollBarVisibility);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(MinHeightProperty);
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            InvalidateMeasure();
            WindowBorder.InvalidateMeasure();
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Loaded);
            if (!rollUp && !restoreRolledUp)
            {
                await RefreshGridLayoutAfterRollTransitionAsync();
            }

            _isRollTransitioning = false;
            QueueSendToBottom();
        }
    }

    private async Task StabilizeRollUpLayoutAsync()
    {
        // Keep the HWND pinned to the animation's final size while the visibility binding
        // and row measurement catch up. Releasing SizeToContent before this pass can expose
        // one intermediate frame at the old content height.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        WindowBorder.InvalidateMeasure();
        UpdateLayout();
    }

    internal static double CalculateRolledUpWindowHeight(
        Thickness windowBorderMargin,
        Thickness windowBorderThickness,
        double visibleHeaderHeight) =>
        Math.Max(
            0,
            windowBorderMargin.Top
            + windowBorderThickness.Top
            + visibleHeaderHeight
            + windowBorderThickness.Bottom
            + windowBorderMargin.Bottom);

    private void RestorePersistedRollUpStateWithoutAnimation()
    {
        if (!_isHoverExpandedFromRollUp)
        {
            return;
        }

        _isHoverExpandedFromRollUp = false;
        if (_isRollTransitioning)
        {
            _restoreRolledUpAfterTransition = true;
        }
        else
        {
            ViewModel.ApplyRollUpState(true);
        }
    }

    private async Task RefreshGridLayoutAfterRollTransitionAsync()
    {
        // Clearing the manual Height does not synchronously finish the SizeToContent pass.
        // Wait until WPF has restored the expanded viewport, then force the recycling panel
        // to realize every row that intersects that viewport. A second render-priority pass
        // handles the HWND resize generated by SizeToContent itself.
        InvalidateMeasure();
        WindowBorder.InvalidateMeasure();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

        InvalidateExpandedGridLayout();
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        InvalidateExpandedGridLayout();
        UpdateLayout();
    }

    private void InvalidateExpandedGridLayout()
    {
        FindVisualChild<VirtualizingCanvas>(IconList)?.InvalidateMeasure();
        IconList.InvalidateMeasure();
        FileList.InvalidateMeasure();
        WindowBorder.InvalidateMeasure();
        InvalidateMeasure();
    }

    private async void OnUseMappingGridModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: false);
    }

    private async void OnUseMappingListModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: true);
    }

    private async Task SwitchMappingViewModeAsync(bool useListMode)
    {
        if (_isMappingViewTransitioning
            || _isRollTransitioning
            || !ViewModel.IsMappingBox
            || ViewModel.IsMappingListMode == useListMode)
        {
            return;
        }

        var visibleBoundsBeforeTransition = GetLayoutVisibleBoundsPixels();
        _mappingViewTransitionVisibleOriginPixels =
            visibleBoundsBeforeTransition.Width > 0 && visibleBoundsBeforeTransition.Height > 0
                ? new Point(visibleBoundsBeforeTransition.Left, visibleBoundsBeforeTransition.Top)
                : null;
        _isMappingViewTransitioning = true;
        var outgoingList = useListMode ? IconList : FileList;
        var incomingList = useListMode ? FileList : IconList;

        try
        {
            var startWidth = Math.Max(MinWidth, ActualWidth);
            var startHeight = Math.Max(MinHeight, ActualHeight);

            // SizeToContent would otherwise apply the target view's desired size in one frame.
            SizeToContent = SizeToContent.Manual;
            // 样式里的 MinWidth/MinHeight 会阻止窗口收拢到标题栏，过渡期间用本地值放行，
            // finally 里 ClearValue 还给样式。
            MinWidth = 0;
            MinHeight = 0;
            Width = startWidth;
            Height = startHeight;

            outgoingList.IsHitTestVisible = false;
            incomingList.IsHitTestVisible = false;
            incomingList.Opacity = 0;
            // 收拢/展开途中可视口临时比内容矮，Auto 滚动条会闪现一下再消失；
            // 过渡期间禁用滚动条（本地值），finally 里 ClearValue 还给 XAML。
            ScrollViewer.SetVerticalScrollBarVisibility(outgoingList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(incomingList, ScrollBarVisibility.Disabled);

            // 两段式时序：旧视图先淡出收拢，全部收回后新视图再展开，两阶段不重叠，
            // 避免两种布局同时显影造成的双影抖动。
            //
            // 收回阶段：旧视图淡出并轻微缩小，窗口高度同步收拢到标题栏下沿。
            var listTop = outgoingList.TranslatePoint(new Point(0, 0), WindowRoot).Y;
            var collapsedHeight = Math.Max(
                36,
                listTop + WindowBorder.Margin.Bottom + WindowBorder.BorderThickness.Bottom);

            var collapseEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            outgoingList.RenderTransformOrigin = new Point(0.5, 0.5);
            var outScale = new ScaleTransform();
            outgoingList.RenderTransform = outScale;
            outScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(150)) { EasingFunction = collapseEase });
            outScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(150)) { EasingFunction = collapseEase });
            outgoingList.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130)) { EasingFunction = collapseEase });

            await AnimateWindowSizeAsync(
                startWidth, startHeight, startWidth, collapsedHeight,
                durationMs: 160, EasingMode.EaseInOut);

            // 模式翻转（同步改绑定，异步写 SQLite 持久化）。
            var modeChangeTask = useListMode
                ? ViewModel.UseMappingListModeCommand.ExecuteAsync(null)
                : ViewModel.UseMappingGridModeCommand.ExecuteAsync(null);

            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.DataBind);

            // 手动 Measure 与真实布局结果不一致（列表会先出滚动条再二次展开），
            // 把尺寸交还给 SizeToContent 做一轮真实布局、直接读稳态尺寸；
            // 同一同步块内立即收回收拢态，不会产生中间渲染帧。
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            UpdateLayout();
            var targetWidth = ActualWidth;
            var targetHeight = ActualHeight;
            SizeToContent = SizeToContent.Manual;
            Width = startWidth;
            Height = collapsedHeight;

            // 展开阶段：窗口从收拢态缓动到稳态尺寸，新视图淡入并轻微放大回位；
            // 结束时尺寸已经是 SizeToContent 的稳态值，恢复时不再回跳。
            var expandEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            incomingList.RenderTransformOrigin = new Point(0.5, 0.5);
            var inScale = new ScaleTransform(0.96, 0.96);
            incomingList.RenderTransform = inScale;
            inScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220))
                {
                    BeginTime = TimeSpan.FromMilliseconds(40),
                    EasingFunction = expandEase
                });
            inScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220))
                {
                    BeginTime = TimeSpan.FromMilliseconds(40),
                    EasingFunction = expandEase
                });
            incomingList.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    BeginTime = TimeSpan.FromMilliseconds(70),
                    EasingFunction = expandEase
                });

            await Task.WhenAll(
                modeChangeTask,
                AnimateWindowSizeAsync(
                    startWidth, collapsedHeight, targetWidth, targetHeight,
                    durationMs: 230, EasingMode.EaseOut),
                // 等淡入与回位动画走完再拆除，避免结尾处状态突变。
                Task.Delay(TimeSpan.FromMilliseconds(310)));
        }
        finally
        {
            outgoingList.BeginAnimation(OpacityProperty, null);
            outgoingList.Opacity = 1;
            outgoingList.RenderTransform = null;
            outgoingList.IsHitTestVisible = true;
            incomingList.BeginAnimation(OpacityProperty, null);
            incomingList.Opacity = 1;
            incomingList.RenderTransform = null;
            incomingList.IsHitTestVisible = true;
            // XAML 里的滚动条可见性本身是本地值，ClearValue 会把它一起抹掉，
            // 这里显式恢复各自的原始值（IconList 恒禁用，FileList 为 Auto）。
            ScrollViewer.SetVerticalScrollBarVisibility(IconList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            ClearValue(MinWidthProperty);
            ClearValue(MinHeightProperty);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            // SizeToContent 的最终布局也必须在过渡保护期内完成，否则靠近工作区
            // 底边的盒子会被 SizeChanged 越界修正向上推，看起来像切换后“弹走”。
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Loaded);
            RestoreMappingViewTransitionOrigin();
            // ClearValue(Size) 后 WPF 还可能把一次 SizeToContent 布局排到当前事件之后。
            // 等到队列空闲再恢复一次，确保延迟 SizeChanged 也无法改变左上锚点。
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ContextIdle);
            RestoreMappingViewTransitionOrigin();
            _mappingViewTransitionVisibleOriginPixels = null;
            _isMappingViewTransitioning = false;
            QueueSendToBottom();
        }
    }

    private void RestoreMappingViewTransitionOrigin()
    {
        if (_mappingViewTransitionVisibleOriginPixels is Point anchoredOrigin)
        {
            MoveToVisibleOriginPixels(anchoredOrigin.X, anchoredOrigin.Y);
        }
    }

    private Task AnimateWindowSizeAsync(
        double startWidth,
        double startHeight,
        double targetWidth,
        double targetHeight,
        int durationMs = 220,
        EasingMode easingMode = EasingMode.EaseOut)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = easingMode };

        var widthAnimation = new DoubleAnimation(startWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };
        var heightAnimation = new DoubleAnimation(startHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };
        heightAnimation.Completed += (_, _) =>
        {
            // Changing the base value before BeginAnimation resizes the native HWND to the
            // target immediately, then WPF paints it back at the start value for the first
            // animation frame. Set the base value only while HoldEnd already presents the
            // target, so removing the animation later cannot produce another size jump.
            SetCurrentValue(WidthProperty, targetWidth);
            SetCurrentValue(HeightProperty, targetHeight);
            completion.TrySetResult();
        };

        BeginAnimation(WidthProperty, widthAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);

        return completion.Task;
    }
}
