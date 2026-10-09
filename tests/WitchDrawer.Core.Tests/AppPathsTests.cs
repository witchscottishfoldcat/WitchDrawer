namespace WitchDrawer.Core.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritabilityProbe_PreservesExistingFileEvenWhenItIsLocked(bool locked)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, ".witchdrawer_write_probe");
            var contents = new byte[] { 0, 1, 42, 255 };
            File.WriteAllBytes(existing, contents);
            using (var held = locked ? new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.None) : null)
            {
                var paths = new AppPaths(root);
                Parallel.For(0, 16, _ => paths.EnsureCreatedAndWritable());
                Assert.Equal([existing], Directory.GetFiles(root));
            }
            Assert.Equal(contents, File.ReadAllBytes(existing));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
