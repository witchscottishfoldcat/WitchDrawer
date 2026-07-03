using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Tests;

// Coverage for the delayed-prune behaviour: a stored item whose backing file is gone is not
// deleted on the first miss. It survives until the threshold of consecutive misses is reached,
// so transient absences (locked file, detached drive, AV quarantine) do not erase user data.
public sealed class DrawerServiceMissingItemTests
{
    [Fact]
    public async Task GetItemsAsync_KeepsMissingItemUntilThresholdThenPrunes()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("src", "ghost.txt", "body");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var exportedPath = Path.Combine(workspace.Root, "exported", "ghost.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(exportedPath)!);

        File.Move(item.StoredPath!, exportedPath);

        // First miss: item stays.
        var first = await workspace.Service.GetItemsAsync(normalBox.Id);
        Assert.Single(first);

        // Second miss: still stays.
        var second = await workspace.Service.GetItemsAsync(normalBox.Id);
        Assert.Single(second);

        // Third consecutive miss: now pruned.
        var third = await workspace.Service.GetItemsAsync(normalBox.Id);
        var remaining = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.Empty(third);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GetItemsAsync_RecoversWhenFileReappearsBeforeThreshold()
    {
        using var workspace = await TestWorkspace.CreateAsync();
        var source = workspace.CreateSourceFile("src", "blink.txt", "body");
        var normalBox = await workspace.GetBoxAsync(BoxType.Normal);
        var item = await workspace.Service.ImportPathAsync(normalBox.Id, source);
        var storedPath = item.StoredPath!;
        var parkedPath = Path.Combine(workspace.Root, "parked", "blink.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(parkedPath)!);

        // Miss once, then put the file back. The miss counter should reset and the item survive.
        File.Move(storedPath, parkedPath);
        _ = await workspace.Service.GetItemsAsync(normalBox.Id);

        File.Move(parkedPath, storedPath);
        var afterRecovery = await workspace.Service.GetItemsAsync(normalBox.Id);
        var stillThere = await workspace.Repository.GetItemsAsync(normalBox.Id);

        Assert.Single(afterRecovery);
        Assert.Single(stillThere);

        // Even after two more reads with the file present, it must not be pruned.
        _ = await workspace.Service.GetItemsAsync(normalBox.Id);
        _ = await workspace.Service.GetItemsAsync(normalBox.Id);
        var finalCheck = await workspace.Repository.GetItemsAsync(normalBox.Id);
        Assert.Single(finalCheck);
    }
}
