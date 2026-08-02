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

    private static DrawerItemViewModel CreateItem(string name, string path)
    {
        var now = DateTimeOffset.UtcNow;
        return new DrawerItemViewModel(
            new DrawerItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                name,
                ItemKind.File,
                SourcePath: null,
                StoredPath: path,
                SortOrder: 0,
                CreatedAt: now,
                UpdatedAt: now),
            iconPixelSize: 16);
    }
}
