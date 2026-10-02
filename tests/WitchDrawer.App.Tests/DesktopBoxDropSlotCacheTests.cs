using System.IO;
using System.Runtime.CompilerServices;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxDropSlotCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedGridDragOver_DoesNotAllocatePerItem(bool internalMove)
    {
        var model = CreateViewModel();
        for (var index = 0; index < 1_000; index++)
        {
            model.Items.Add(CreateItem(model, index % 40, index / 40));
        }

        var movingId = internalMove ? model.Items[0].Id : (Guid?)null;
        void DragOver()
        {
            var raw = model.GetGridSlot(10, 10, 2_000, 2_000);
            model.TryGetAvailableDropSlot(raw.Column, raw.Row, movingId, out var slot);
            model.ShowDragPreview(slot.Column, slot.Row);
        }

        // Include the real pre-preview calls: deduplicating ShowDragPreview alone
        // cannot avoid the old occupancy HashSet and extent scans on every event.
        for (var index = 0; index < 100; index++)
        {
            DragOver();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            DragOver();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4_096, $"Repeated DragOver allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void DirectPositionChanges_InvalidateOccupancyAndExtentsWithoutLayoutVersionChange()
    {
        var model = CreateViewModel();
        var first = CreateItem(model, 0, 0);
        model.Items.Add(first);
        model.Items.Add(CreateItem(model, 1, 0));
        var version = model.GridLayoutVersion;
        var width = model.LayoutSettings.ItemSlotWidth;
        Assert.Equal((2, 0), model.GetAvailableDropSlot(0, 0));
        Assert.Equal(2, model.GetGridSlot(2 * width - 5, 10, 1_000, 1_000).Column);

        first.SetGridPosition(3, 2, model.LayoutSettings);

        Assert.Equal(version, model.GridLayoutVersion);
        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0));
        Assert.Equal(1, model.GetGridSlot(2 * width - 5, 10, 1_000, 1_000).Column);
        Assert.Equal(3, model.GetGridSlot(10, 3 * model.LayoutSettings.ItemSlotHeight - 5, 1_000, 1_000).Row);
    }

    [Fact]
    public void SameCountReplacementAndReset_InvalidateCachedSlots()
    {
        var model = CreateViewModel();
        model.Items.Add(CreateItem(model, 0, 0));
        model.Items.Add(CreateItem(model, 1, 0));
        Assert.Equal((2, 0), model.GetAvailableDropSlot(0, 0));

        model.Items[0] = CreateItem(model, 2, 0);
        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0));

        var replacement = CreateItem(model, 0, 0);
        model.Items.ReplaceAll([replacement, CreateItem(model, 3, 0)]);
        Assert.Equal((1, 0), model.GetAvailableDropSlot(0, 0));
        replacement.SetGridPosition(4, 1, model.LayoutSettings);
        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0));

        model.Items.Clear();
        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0));
    }

    [Fact]
    public void MovingItemChanges_ExcludeOnlyItsOwnOccupancy()
    {
        var model = CreateViewModel();
        var first = CreateItem(model, 0, 0);
        var second = CreateItem(model, 1, 0);
        model.Items.Add(first);
        model.Items.Add(second);

        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0, first.Id));
        Assert.Equal((1, 0), model.GetAvailableDropSlot(0, 0, second.Id));
        Assert.Equal((2, 0), model.GetAvailableDropSlot(0, 0));

        // A damaged/overlapping layout still has another item's occupancy in the
        // source cell; removing a source cell directly from a HashSet is incorrect.
        second.SetGridPosition(0, 0, model.LayoutSettings);
        Assert.Equal((1, 0), model.GetAvailableDropSlot(0, 0, first.Id));
    }

    [Fact]
    public void FixedCapacityAndBounds_AreRecheckedAfterSizeOrCollectionChanges()
    {
        var model = CreateViewModel();
        var first = CreateItem(model, 0, 0);
        model.Items.Add(first);
        model.Items.Add(CreateItem(model, 1, 0));
        model.ApplySizeMode(new BoxSizeModeState(true, 2, 1));

        Assert.False(model.HasFreeSlotForDrop());
        Assert.False(model.TryGetAvailableDropSlot(9, 9, null, out _));
        Assert.True(model.HasFreeSlotForDrop(first.Id));
        Assert.True(model.TryGetAvailableDropSlot(9, 9, first.Id, out var moveSlot));
        Assert.Equal((0, 0), moveSlot);

        model.ApplySizeMode(new BoxSizeModeState(true, 3, 1));
        Assert.True(model.HasFreeSlotForDrop());
        Assert.True(model.TryGetAvailableDropSlot(9, 9, null, out var expandedSlot));
        Assert.Equal((2, 0), expandedSlot);

        model.Items.Add(CreateItem(model, 2, 0));
        Assert.False(model.HasFreeSlotForDrop());
        Assert.False(model.TryGetAvailableDropSlot(9, 9, null, out _));
        model.Items.RemoveAt(2);
        Assert.True(model.TryGetAvailableDropSlot(9, 9, null, out var removedSlot));
        Assert.Equal((2, 0), removedSlot);
    }

    [Fact]
    public void MovingTheOutermostItem_PreservesAdaptiveSearchWrap()
    {
        var model = CreateViewModel();
        for (var column = 0; column < 5; column++)
        {
            model.Items.Add(CreateItem(model, column, 0));
        }

        var outside = CreateItem(model, 9, 1);
        model.Items.Add(outside);
        Assert.Equal((5, 0), model.GetAvailableDropSlot(0, 0));
        Assert.Equal((0, 1), model.GetAvailableDropSlot(0, 0, outside.Id));
    }

    [Fact]
    public void ItemOrderAndCountCache_TracksMovesReplacementAndReset()
    {
        var model = CreateViewModel();
        var first = CreateItem(model, 0, 0);
        var second = CreateItem(model, 1, 0);
        model.Items.Add(first);
        model.Items.Add(second);
        Assert.Equal(0, model.GetDropItemIndex(first.Id));
        Assert.Equal(1, model.GetDropItemCount(first.Id));

        model.Items.Move(0, 1);
        Assert.Equal(1, model.GetDropItemIndex(first.Id));
        Assert.Equal(0, model.GetDropItemIndex(second.Id));

        model.Items[1] = CreateItem(model, 3, 0);
        Assert.Equal(-1, model.GetDropItemIndex(first.Id));
        Assert.Equal(2, model.GetDropItemCount(first.Id));

        model.Items.ReplaceAll([first]);
        Assert.Equal(0, model.GetDropItemIndex(first.Id));
        Assert.Equal(0, model.GetDropItemCount(first.Id));
    }

    [Fact]
    public async Task ExpandedPreview_RetainsFiftyMillisecondLayoutSettlingWindow()
    {
        var model = CreateViewModel();
        model.Items.Add(CreateItem(model, 0, 0));
        var expanded = model.GetGridSlot(model.LayoutSettings.ItemSlotWidth - 5, 10, 1_000, 1_000);
        Assert.Equal((1, 0), expanded);
        model.ShowDragPreview(expanded.Column, expanded.Row);
        Assert.Equal(expanded, model.GetGridSlot(10, 10, 1_000, 1_000));

        await Task.Delay(100);
        Assert.Equal((0, 0), model.GetGridSlot(10, 10, 1_000, 1_000));
    }

    [Fact]
    public void ReleasingHiddenItems_DoesNotRetainOldItemViewModels()
    {
        var model = CreateViewModel();
        var removed = PopulateDropCacheAndReleaseItems(model);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(removed.TryGetTarget(out _));
        Assert.Equal((0, 0), model.GetAvailableDropSlot(0, 0));
        GC.KeepAlive(model);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<DrawerItemViewModel> PopulateDropCacheAndReleaseItems(DesktopBoxViewModel model)
    {
        var item = CreateItem(model, 0, 0);
        model.Items.Add(item);
        model.GetAvailableDropSlot(0, 0, item.Id);
        var weak = new WeakReference<DrawerItemViewModel>(item);
        model.ReleaseHiddenWindowItems();
        return weak;
    }

    private static DesktopBoxViewModel CreateViewModel()
    {
        // The services remain uninitialized. These drag/layout tests never touch
        // user data, extract icons, or perform filesystem/database operations.
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "WitchDrawerDropCache", Guid.NewGuid().ToString("N")));
        var repository = new DrawerRepository(paths.DatabasePath);
        var now = DateTimeOffset.UtcNow;
        var box = new Box(Guid.NewGuid(), "拖放测试", BoxType.Normal, null, 0, now, now);
        return new DesktopBoxViewModel(box, new DrawerService(paths, repository), new TodoService(repository),
            new NoOpLauncher(), new NoOpShellNotifier(), NullAppLogger.Instance, BoxVisualStyle.Modern);
    }

    private static DrawerItemViewModel CreateItem(DesktopBoxViewModel model, int column, int row)
    {
        var now = DateTimeOffset.UtcNow;
        return new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), model.BoxId, "item", ItemKind.File,
            null, null, 0, now, now, column, row));
    }

    private sealed class NoOpLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpShellNotifier : IShellChangeNotifier
    {
        public Task NotifyItemImportedAsync(DrawerItem item, IAppLogger logger) => Task.CompletedTask;
        public void NotifyCreated(string path, bool isDirectory) { }
        public void NotifyFolderItemCreated(string path, bool isDirectory) { }
    }
}
