using System.Windows;
using System.Windows.Controls;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// 拖放目标端：DragOver/Leave/Drop 处理、三形态落点预览（网格槽/列表插入线/封面格）与视觉复位。
/// </summary>
public partial class DesktopBoxWindow
{
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // 紧跟 DragLeave 的 DragOver 说明只是 resize churn：取消待执行的复位。
        CancelPendingDragLeaveReset();
        CancelPendingHoverRollUp();
        if (ViewModel.IsRolledUp)
        {
            RequestHoverExpand();
        }

        // OLE 拖拽期间 MouseEnter/MouseLeave 不会触发：拖拽悬停也要取消隐藏，
        // 否则拖文件到高度透明的盒上时落点不可见。重复 DragOver 由管理器侧去重。
        if (_autoHideEnabled)
        {
            AutoHideHoverEntered?.Invoke(this, EventArgs.Empty);
        }

        if (ViewModel.IsTodoBox)
        {
            ViewModel.IsDragOver = false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var acceptsDrop = false;
        var showPreview = false;
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            acceptsDrop = TryGetInternalDragPayload(e.Data, out var payload);
            // 固定模式（硬约束）：盒已满时拒绝拖入。
        if (acceptsDrop && !ViewModel.HasFreeSlotForDrop(
                payload.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null))
        {
            acceptsDrop = false;
        }

        // 排序模式的落点由排序键决定（盒内拖动为空操作），槽位预览会误导：
        // 只保留盒子高亮，不显示落点框。
        showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? DragDropEffects.Move : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, payload);
            }
        }
        else
        {
            var dropEffect = ChooseFileDropEffect(e.AllowedEffects);
            acceptsDrop = e.Data.GetDataPresent(DataFormats.FileDrop) && dropEffect != DragDropEffects.None;
            // 固定模式（硬约束）：盒已满时拒绝拖入文件。
            if (acceptsDrop && !ViewModel.HasFreeSlotForDrop())
            {
                acceptsDrop = false;
            }

            showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? dropEffect : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, null);
            }
        }

        if (!showPreview)
        {
            ViewModel.HideDragPreview();
            HideMappingListDropIndicator();
        }

        ViewModel.IsDragOver = acceptsDrop;

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        ScheduleHoverRollUp();
        // SizeToContent 窗口随拖拽预览在指针下方生长时，OLE 会补发 DragLeave/DragEnter 对
        // （churn）。若在此同步复位，就会出现"复位→下一帧 DragOver 再显示→再复位"的疯狂频闪。
        // 改为延迟复位：churn 场景紧跟的 DragOver 会取消它；真正离开/取消时没有后续
        // DragOver，复位在极短延迟后生效（肉眼不可辨）。
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _dragLeaveResetCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        FireAndForget.Run(
                ResetDragVisualStateAfterSettlingAsync(cts),
                ViewModel.Logger,
                $"Failed to reset drag visual state for box {ViewModel.BoxId:N}.");
    }

    private async Task ResetDragVisualStateAfterSettlingAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(90, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            ResetDragVisualState();
        }
    }

    private void CancelPendingDragLeaveReset()
    {
        var cts = Interlocked.Exchange(ref _dragLeaveResetCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (ViewModel.IsTodoBox)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ResetDragVisualState();
            return;
        }

        if (!e.Data.GetDataPresent(InternalDrawerItemDragFormat)
            && !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;
        try
        {
            if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
            {
                if (TryGetInternalDragPayload(e.Data, out var payload))
                {
                    var slot = GetDropSlot(e, payload);
                    if (slot is null)
                    {
                        // 固定模式盒已满：拒绝落放，不标记为内部移动，项目保留在原盒。
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    e.Effects = DragDropEffects.Move;
                    // Mark synchronously (same object instance, in-process) so the source
                    // box sees it immediately after DoDragDrop returns and treats this as
                    // an internal move/rearrange rather than a move-out to the desktop.
                    payload.WasDroppedInsideWitchDrawer = true;
                    payload.TargetBoxId = ViewModel.BoxId;
                    FireAndForget.Run(
                        CompleteInternalDropAsync(payload, slot.Value),
                        ViewModel.Logger,
                        $"Failed to complete internal drop for box {ViewModel.BoxId:N}.");
                }

                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var slot = GetDropSlot(e);
                if (slot is null)
                {
                    // 固定模式盒已满：拒绝导入，文件保持原样。
                    e.Effects = DragDropEffects.None;
                    return;
                }

                e.Effects = paths.Length > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                ResetDragVisualState();
                ResetDragCursor();
                // ImportPathsAsync already reloads the box internally; no extra LoadAsync here.
                var importedIds = await ViewModel.ImportPathsAsync(paths, slot.Value.Column, slot.Value.Row);
                e.Effects = importedIds.Count > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                var lastImportedId = importedIds.LastOrDefault();
                var importedItem = lastImportedId != Guid.Empty
                    ? ViewModel.Items.FirstOrDefault(candidate => candidate.Id == lastImportedId)
                    : null;
                if (importedItem is not null)
                {
                    importedItem.ReloadIconIfNeeded();
                    // Only keep keyboard selection while this box actually has focus.
                    // External Explorer drops often leave the window non-activated; a sticky
                    // SelectedItem then cannot be cleared by clicking the desktop.
                    if (IsActive)
                    {
                        ActiveItemsList.SelectedItem = importedItem;
                        _keyboardDeleteTarget = importedItem;
                        ActiveItemsList.Focus();
                    }
                    else
                    {
                        ClearItemSelection();
                    }
                }
            }
        }
        finally
        {
            ResetDragVisualState();
            ResetDragCursor();
        }
    }

    private (int Column, int Row)? GetDropSlot(DragEventArgs e, DesktopBoxDragPayload? payload = null)
    {
        var movingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
        if (ViewModel.IsMappingListMode)
        {
            return (0, GetMappingListDropIndex(e, movingItemId));
        }

        if (ViewModel.IsDrawerCollapsed)
        {
            // The collapsed drawer cover is not the item grid (the IconList is hidden and
            // has zero size), so pointer coordinates cannot select a grid cell. Append
            // after the last item, the same fallback the mapping list view uses.
            return ViewModel.GetListDropSlot(movingItemId);
        }

        var itemList = ActiveItemsList;
        var point = e.GetPosition(itemList);
        var padding = itemList.Padding;
        var rawSlot = ViewModel.GetGridSlot(
            point.X - padding.Left,
            point.Y - padding.Top,
            Math.Max(0, itemList.ActualWidth - padding.Left - padding.Right),
            Math.Max(0, itemList.ActualHeight - padding.Top - padding.Bottom));

        // 固定模式（硬约束）：盒内找不到空位时返回 null，调用方据此拒绝拖放。
        return ViewModel.TryGetAvailableDropSlot(rawSlot.Column, rawSlot.Row, movingItemId, out var slot)
            ? slot
            : null;
    }

    private int GetMappingListDropIndex(DragEventArgs e, Guid? movingItemId)
    {
        var sourceIndex = movingItemId is Guid itemId
            ? ViewModel.Items.ToList().FindIndex(item => item.Id == itemId)
            : -1;
        var source = e.OriginalSource as DependencyObject;
        var container = source is null
            ? null
            : ItemsControl.ContainerFromElement(FileList, source) as ListBoxItem;
        if (container is null)
        {
            return ViewModel.Items.Count - (sourceIndex >= 0 ? 1 : 0);
        }

        var hoveredIndex = FileList.ItemContainerGenerator.IndexFromContainer(container);
        var insertAfter = e.GetPosition(container).Y >= container.ActualHeight / 2;
        return CalculateListInsertionIndex(
            ViewModel.Items.Count,
            sourceIndex,
            hoveredIndex,
            insertAfter);
    }

    internal static int CalculateListInsertionIndex(
        int itemCount,
        int sourceIndex,
        int hoveredIndex,
        bool insertAfter)
    {
        itemCount = Math.Max(0, itemCount);
        var hasSource = sourceIndex >= 0 && sourceIndex < itemCount;
        var boundary = Math.Clamp(hoveredIndex, 0, Math.Max(0, itemCount - 1))
            + (insertAfter ? 1 : 0);
        if (hasSource && sourceIndex < boundary)
        {
            boundary--;
        }

        var remainingCount = itemCount - (hasSource ? 1 : 0);
        return Math.Clamp(boundary, 0, remainingCount);
    }

    private void ShowDropPreview(DragEventArgs e, DesktopBoxDragPayload? payload)
    {
        if (ViewModel.IsMappingListMode)
        {
            var movingItemId = payload?.SourceBoxId == ViewModel.BoxId
                ? payload.ItemId
                : (Guid?)null;
            ViewModel.HideDragPreview();
            ShowMappingListDropIndicator(e, movingItemId);
            return;
        }

        HideMappingListDropIndicator();
        if (ViewModel.IsDrawerCollapsed)
        {
            var coverMovingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
            ShowDrawerCoverDropPreview(coverMovingItemId);
            return;
        }

        var slot = GetDropSlot(e, payload);
        if (slot is null)
        {
            // 固定模式盒已满：不显示落点预览，DragOver 已给出禁止光标。
            ViewModel.HideDragPreview();
            return;
        }

        ViewModel.ShowDragPreview(slot.Value.Column, slot.Value.Row);
    }

    private void ShowMappingListDropIndicator(DragEventArgs e, Guid? movingItemId)
    {
        var insertionIndex = GetMappingListDropIndex(e, movingItemId);
        var remainingItems = ViewModel.Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .ToList();

        FrameworkElement? boundaryContainer = null;
        var useContainerBottom = false;
        if (remainingItems.Count > 0)
        {
            if (insertionIndex < remainingItems.Count)
            {
                boundaryContainer = FileList.ItemContainerGenerator.ContainerFromItem(
                    remainingItems[insertionIndex]) as FrameworkElement;
            }
            else
            {
                boundaryContainer = FileList.ItemContainerGenerator.ContainerFromItem(
                    remainingItems[^1]) as FrameworkElement;
                useContainerBottom = true;
            }
        }

        Point boundaryPoint;
        if (boundaryContainer is not null)
        {
            boundaryPoint = boundaryContainer.TranslatePoint(
                new Point(0, useContainerBottom ? boundaryContainer.ActualHeight : 0),
                MappingListDropOverlay);
        }
        else
        {
            var source = e.OriginalSource as DependencyObject;
            var hoveredContainer = source is null
                ? null
                : ItemsControl.ContainerFromElement(FileList, source) as FrameworkElement;
            boundaryPoint = hoveredContainer is null
                ? FileList.TranslatePoint(
                    new Point(0, FileList.Padding.Top),
                    MappingListDropOverlay)
                : hoveredContainer.TranslatePoint(
                    new Point(
                        0,
                        e.GetPosition(hoveredContainer).Y >= hoveredContainer.ActualHeight / 2
                            ? hoveredContainer.ActualHeight
                            : 0),
                    MappingListDropOverlay);
        }

        var listOrigin = FileList.TranslatePoint(new Point(0, 0), MappingListDropOverlay);
        var horizontalInset = Math.Max(6, FileList.Padding.Left);
        Canvas.SetLeft(MappingListInsertionIndicator, listOrigin.X + horizontalInset);
        Canvas.SetTop(MappingListInsertionIndicator, boundaryPoint.Y - 1);
        MappingListInsertionIndicator.Width = Math.Max(
            MappingListInsertionIndicator.MinWidth,
            FileList.ActualWidth - horizontalInset - Math.Max(6, FileList.Padding.Right));
        MappingListDropOverlay.Visibility = Visibility.Visible;
    }

    private void HideMappingListDropIndicator()
    {
        MappingListDropOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowDrawerCoverDropPreview(Guid? movingItemId)
    {
        // Dropped items append after the last item (see GetDropSlot), so the preview
        // frame marks the exact cover cell the item will occupy -- the same
        // "frame == landing spot" contract the normal grid boxes have.
        var insertIndex = ViewModel.Items.Count(item => movingItemId is null || item.Id != movingItemId.Value);
        if (insertIndex >= ViewModel.DrawerCoverCapacity
            || DrawerCoverItems.ActualWidth <= 0
            || DrawerCoverItems.ActualHeight <= 0)
        {
            // The item lands in the overflow popup (or the cover is not measured yet):
            // there is no cover cell to point at, keep just the box highlight.
            ViewModel.HideDragPreview();
            return;
        }

        var cellRect = CalculateCoverCellRect(
            insertIndex,
            ViewModel.DrawerCoverColumns,
            ViewModel.DrawerCoverRows,
            DrawerCoverItems.ActualWidth,
            DrawerCoverItems.ActualHeight,
            ViewModel.LayoutSettings.ItemSpacing);
        var origin = DrawerCoverItems.TranslatePoint(
            new Point(cellRect.Left, cellRect.Top),
            DragPreviewCanvas);
        ViewModel.ShowDragPreviewAt(origin.X, origin.Y, cellRect.Width, cellRect.Height);
    }

    internal static Rect CalculateCoverCellRect(
        int cellIndex,
        int columns,
        int rows,
        double surfaceWidth,
        double surfaceHeight,
        double inset)
    {
        var safeColumns = Math.Max(1, columns);
        var safeRows = Math.Max(1, rows);
        var cellWidth = surfaceWidth / safeColumns;
        var cellHeight = surfaceHeight / safeRows;
        var safeIndex = Math.Max(0, cellIndex);
        var cellColumn = safeIndex % safeColumns;
        var cellRow = safeIndex / safeColumns;
        return new Rect(
            (cellColumn * cellWidth) + inset,
            (cellRow * cellHeight) + inset,
            Math.Max(1, cellWidth - (inset * 2)),
            Math.Max(1, cellHeight - (inset * 2)));
    }

    private async Task CompleteInternalDropAsync(DesktopBoxDragPayload payload, (int Column, int Row) slot)
    {
        var moved = false;
        try
        {
            moved = await ViewModel.DropDrawerItemAsync(payload.ItemId, slot.Column, slot.Row);
            if (moved)
            {
                MarkDroppedInsideWitchDrawer(payload);
                SelectItem(payload.ItemId);
            }
        }
        finally
        {
            payload.CompleteDrop(moved);
        }
    }

    private void ResetDragVisualState()
    {
        // 立即复位（落放/拖拽结束/全局清理）：任何延迟复位都取消。
        CancelPendingDragLeaveReset();
        ViewModel.HideDragPreview();
        HideMappingListDropIndicator();
        ViewModel.IsDragOver = false;

        // 同步自动隐藏的悬停态：指针仍在盒上（如点击触发的全局清理）保持 reveal；
        // 真正拖离或落放后指针不在盒上则恢复隐藏。拖拽中 WPF 不发鼠标事件，
        // 残留的 reveal 会在下一次鼠标移动触发 MouseEnter/Leave 时自行校正。
        if (_autoHideEnabled)
        {
            if (IsMouseOver)
            {
                AutoHideHoverEntered?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                AutoHideHoverLeft?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static void ResetAllDragVisualStates()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.ResetDragVisualState();
        }
    }

    private static DragDropEffects ChooseFileDropEffect(DragDropEffects allowedEffects)
    {
        if ((allowedEffects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return DragDropEffects.Move;
        }

        return (allowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy
            ? DragDropEffects.Copy
            : (allowedEffects & DragDropEffects.Link) == DragDropEffects.Link
                ? DragDropEffects.Link
                : DragDropEffects.None;
    }
}
