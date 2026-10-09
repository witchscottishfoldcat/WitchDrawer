using System.IO;

namespace WitchDrawer.App.Tests;

public sealed class TestDirectoryCleanupTests
{
    [Fact]
    public async Task SharingViolation_RetriesUntilTheOwnerReleasesTheFile()
    {
        var root = CreateRoot();
        Task? cleanup = null;
        try
        {
            using (var file = new FileStream(Path.Combine(root, "locked.db"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                cleanup = TestDirectoryCleanup.DeleteAsync(root);
                Assert.False(cleanup.IsCompleted);
            }
            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (cleanup is not null) await cleanup;
            await TestDirectoryCleanup.DeleteAsync(root);
        }
    }

    [Fact]
    public async Task PersistentSharingViolation_IsStillReportedAsFailure()
    {
        var root = CreateRoot();
        try
        {
            using var file = new FileStream(Path.Combine(root, "locked.db"), FileMode.Create, FileAccess.Write, FileShare.None);
            await Assert.ThrowsAsync<IOException>(() => TestDirectoryCleanup.DeleteAsync(root));
            Assert.True(Directory.Exists(root));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(root); }
    }

    [Fact]
    public async Task Cleanup_RejectsDirectoriesOutsideItsTestWorkspace()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TestDirectoryCleanup.DeleteAsync(Path.GetTempPath()));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
