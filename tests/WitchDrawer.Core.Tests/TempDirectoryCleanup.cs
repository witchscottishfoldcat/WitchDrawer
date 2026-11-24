namespace WitchDrawer.Core.Tests;

internal static class TempDirectoryCleanup
{
    public static void Delete(string path)
    {
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!directory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected a directory under the test temp root.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException exception) when (attempt < 10 && (exception.HResult & 0xffff) is 32 or 33)
            {
                Thread.Sleep(100);
            }
        }
    }
}
