using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class DrawerItemSortTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(16, 4)]
    [InlineData(17, 5)]
    [InlineData(80, 5)]
    public void SecondaryDrawer_UsesStableAdaptiveColumnBands(int itemCount, int expectedColumns)
    {
        Assert.Equal(expectedColumns, DesktopBoxViewModel.CalculateDrawerSecondaryColumns(itemCount));
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(12, 4, 3)]
    [InlineData(27, 5, 6)]
    public void SecondaryDrawer_HeightFollowsItsActualRowCount(
        int itemCount,
        int columns,
        int expectedRows)
    {
        Assert.Equal(
            expectedRows,
            DesktopBoxViewModel.CalculateDrawerSecondaryRows(itemCount, columns));
    }

    [Fact]
    public void SecondaryDrawer_SortsByNameSizeTypeAndModifiedDate()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerSortTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var alphaPath = Path.Combine(root, "alpha.txt");
            var betaPath = Path.Combine(root, "beta.bin");
            File.WriteAllBytes(alphaPath, [1]);
            File.WriteAllBytes(betaPath, [1, 2, 3, 4]);
            File.SetLastWriteTimeUtc(alphaPath, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(betaPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var alpha = CreateItem("Alpha", alphaPath);
            var beta = CreateItem("Beta", betaPath);
            DrawerItemViewModel[] source = [beta, alpha];

            Assert.Equal(
                ["Alpha", "Beta"],
                DesktopBoxViewModel.SortDrawerItems(source, DrawerItemSortMode.Name)
                    .Select(item => item.DisplayName));
            Assert.Equal(
                ["Alpha", "Beta"],
                DesktopBoxViewModel.SortDrawerItems(source, DrawerItemSortMode.Size)
                    .Select(item => item.DisplayName));
            Assert.Equal(
                ["Beta", "Alpha"],
                DesktopBoxViewModel.SortDrawerItems(source, DrawerItemSortMode.ItemType)
                    .Select(item => item.DisplayName));
            Assert.Equal(
                ["Beta", "Alpha"],
                DesktopBoxViewModel.SortDrawerItems(source, DrawerItemSortMode.ModifiedDate)
                    .Select(item => item.DisplayName));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(DrawerItemSortMode.Free)]
    [InlineData(DrawerItemSortMode.Name)]
    [InlineData(DrawerItemSortMode.ItemType)]
    public void SortWithoutFileMetadata_DoesNotReadTheFileSystem(DrawerItemSortMode sortMode)
    {
        // The source paths need not exist or respond: these modes use only stored text.
        var beta = CreateItem("Beta", @"\\unavailable-server\share\beta.bin");
        var alpha = CreateItem("Alpha", @"\\unavailable-server\share\alpha.txt");
        DrawerItemViewModel[] source = [beta, alpha];
        var metadataReads = 0;

        var sorted = DesktopBoxViewModel.SortDrawerItems(source, sortMode, (_, _) =>
        {
            metadataReads++;
            throw new IOException("File metadata must not be queried for this mode.");
        });

        Assert.Equal(0, metadataReads);
        Assert.Equal(
            sortMode == DrawerItemSortMode.Name ? [alpha, beta] : source,
            sorted);
        if (sortMode == DrawerItemSortMode.Free)
        {
            Assert.Same(source, sorted);
        }
    }

    [Fact]
    public void TypeSort_UsesFolderAndExtensionlessLabelsWithNameTieBreaks()
    {
        var folder = CreateItem("Folder", @"C:\missing\folder.txt", ItemKind.Directory);
        var plain = CreateItem("Plain", @"C:\missing\plain");
        var alpha = CreateItem("Alpha", @"C:\missing\alpha.TXT");
        var beta = CreateItem("Beta", @"C:\missing\beta.txt");
        var labels = new[]
        {
            (Item: folder, Type: "文件夹"),
            (Item: plain, Type: "文件"),
            (Item: beta, Type: ".txt"),
            (Item: alpha, Type: ".TXT")
        };
        var expected = labels
            .OrderBy(entry => entry.Type, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Item);

        Assert.Equal(
            expected,
            DesktopBoxViewModel.SortDrawerItems(labels.Select(entry => entry.Item).ToArray(),
                DrawerItemSortMode.ItemType));
    }

    [Theory]
    [InlineData(DrawerItemSortMode.Size)]
    [InlineData(DrawerItemSortMode.ModifiedDate)]
    public void MetadataSort_ReadsEachItemOnceForTheRequestedMode(DrawerItemSortMode sortMode)
    {
        var alpha = CreateItem("Alpha", @"C:\missing\alpha.txt");
        var beta = CreateItem("Beta", @"C:\missing\beta.txt");
        DrawerItemViewModel[] source = [beta, alpha];
        var reads = new List<(Guid ItemId, DrawerItemSortMode Mode)>();
        var sorted = DesktopBoxViewModel.SortDrawerItems(source, sortMode, (item, mode) =>
        {
            reads.Add((item.Id, mode));
            return item == alpha
                ? (1, new DateTime(2024, 1, 1))
                : (2, new DateTime(2025, 1, 1));
        });

        Assert.Equal(source.Select(item => (item.Id, sortMode)), reads);
        Assert.Equal(sortMode == DrawerItemSortMode.Size ? [alpha, beta] : source, sorted);
    }

    [Fact]
    public void SizeSort_EmptyDirectoriesRemainBeforeMissingFiles()
    {
        var folder = CreateItem("Folder", @"C:\missing\folder", ItemKind.Directory);
        var missing = CreateItem("Missing", @"C:\missing\file.txt");

        Assert.Equal(
            [folder, missing],
            DesktopBoxViewModel.SortDrawerItems([missing, folder], DrawerItemSortMode.Size));
    }

    private static DrawerItemViewModel CreateItem(string name, string path, ItemKind kind = ItemKind.File)
    {
        var now = DateTimeOffset.UtcNow;
        return new DrawerItemViewModel(
            new DrawerItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                name,
                kind,
                SourcePath: null,
                StoredPath: path,
                SortOrder: 0,
                CreatedAt: now,
                UpdatedAt: now),
            iconPixelSize: 16);
    }
}
