namespace WitchDrawer.Core.Services;

internal static class PathSafety
{
    public static string GetFullExistingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Dropped path does not exist.", fullPath);
        }

        return fullPath;
    }

    public static void EnsureChildPath(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath);
        var prefix = root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Target path is outside the allowed storage root: {candidate}");
        }

        EnsureNoReparsePoints(root);
        EnsureNoReparsePointsAlongExistingPath(candidate, root);
    }

    public static void EnsureNoReparsePoints(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Reparse points are not supported in storage paths: {path}");
        }
    }

    private static void EnsureNoReparsePointsAlongExistingPath(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        if (relative == ".")
        {
            return;
        }

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            EnsureNoReparsePoints(current);
        }
    }
}
