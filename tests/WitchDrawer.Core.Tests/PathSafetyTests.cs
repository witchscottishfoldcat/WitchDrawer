using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData(@"C:\data\Boxes\item.txt", @"\\?\C:\data\Boxes\item.txt")]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData(@"\\server\share\folder\item.txt", @"\\?\UNC\server\share\folder\item.txt")]
    [InlineData(@"\\server\share\", @"\\?\unc\server\share\")]
    public void Comparison_RecognizesExtendedDriveAndUncAliases(string ordinary, string extended)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PathSafety.PathsEqual(ordinary, extended));
        Assert.True(PathSafety.PathsEqual(extended, ordinary));
        Assert.Equal(PathSafety.GetComparisonPath(ordinary), PathSafety.GetComparisonPath(extended), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\data\Boxes\item.txt", @"\\?\C:\data\Boxes", true)]
    [InlineData(@"\\?\C:\data\Boxes\item.txt", @"C:\data\Boxes", true)]
    [InlineData(@"\\?\C:\data\Boxes-other\item.txt", @"C:\data\Boxes", false)]
    [InlineData(@"\\?\UNC\server\share\folder\item.txt", @"\\server\share\folder", true)]
    [InlineData(@"\\?\UNC\server\share-other\item.txt", @"\\server\share", false)]
    public void DescendantComparison_UsesTheSameNamespaceAndDirectoryBoundary(string path, string root, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, PathSafety.IsSameOrDescendant(path, root));
    }

    [Theory]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\file.txt")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\file.txt")]
    [InlineData(@"\\.\C:\file.txt")]
    public void Comparison_RejectsDeviceNamespacesThatCannotBeComparedToStorage(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<InvalidOperationException>(() => PathSafety.GetComparisonPath(path));
    }

    [Fact]
    public void EnsureChildPath_AcceptsMixedNamespacesButRejectsTheRootAndSiblings()
    {
        if (!OperatingSystem.IsWindows()) return;
        var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawer.PathSafety.Tests"));
        var root = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var child = Path.Combine(root, "nested", "item.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(child)!);
            File.WriteAllText(child, "content");
            PathSafety.EnsureChildPath(root, @"\\?\" + child);
            PathSafety.EnsureChildPath(@"\\?\" + root, child);
            Assert.Throws<InvalidOperationException>(() => PathSafety.EnsureChildPath(root, @"\\?\" + root));
            Assert.Throws<InvalidOperationException>(() => PathSafety.EnsureChildPath(root, @"\\?\" + root + "-other\\item.txt"));
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }
}
