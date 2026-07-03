namespace WitchDrawer.Core.Services;

internal static class FileNameService
{
    public static string GetUniqueDestinationPath(string directory, string originalName, bool isDirectory)
    {
        Directory.CreateDirectory(directory);

        var candidate = Path.Combine(directory, originalName);
        if (!Exists(candidate, isDirectory))
        {
            return candidate;
        }

        var name = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(originalName);

        for (var index = 1; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!Exists(candidate, isDirectory))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name for {originalName}.");
    }

    // Windows file systems are case-insensitive, so File.Exists("report.txt") already returns true
    // when "Report.TXT" is on disk. The EnumateFileSystemEntries fallback guards against edge
    // cases (e.g. long paths or alternative separators) where the direct probe can miss an
    // existing entry of different casing, which would otherwise produce a silent overwrite.
    private static bool Exists(string path, bool isDirectory)
    {
        if (isDirectory ? Directory.Exists(path) : File.Exists(path))
        {
            return true;
        }

        var parent = Path.GetDirectoryName(path);
        var leaf = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(parent, leaf)
                .Any(existing => string.Equals(Path.GetFileName(existing), leaf, StringComparison.OrdinalIgnoreCase));
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
