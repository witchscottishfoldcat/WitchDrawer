using System.IO;

namespace WitchDrawer.App.Tests;

internal static class TestDirectoryCleanup
{
    internal static void Delete(string path) => DeleteAsync(path).GetAwaiter().GetResult();

    internal static async Task DeleteAsync(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerTests")) + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cleanup is restricted to an owned WitchDrawerTests workspace.", nameof(path));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(resolvedPath, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException exception) when (attempt < 5 && (exception.HResult & 0xffff) is 32 or 33)
            {
                // Owned tasks must already be awaited. Allow transient Windows file locks
                // to settle, but leave persistent resource leaks visible as test failures.
                await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
            }
        }
    }
}
