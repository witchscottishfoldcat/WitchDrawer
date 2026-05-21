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

    private static bool Exists(string path, bool isDirectory)
    {
        return isDirectory ? Directory.Exists(path) : File.Exists(path);
    }
}

