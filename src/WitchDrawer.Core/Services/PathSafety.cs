namespace WitchDrawer.Core.Services;

internal static class PathSafety
{
    /// <summary>
    /// Normalizes DOS-drive and UNC spellings for comparisons only. Keep the
    /// original full path for I/O so extended paths retain long-path semantics.
    /// Other device namespaces cannot safely be compared with storage paths.
    /// This does not resolve filesystem aliases such as 8.3 names or hard links.
    /// </summary>
    internal static string GetComparisonPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    fullPath = Path.GetFullPath(@"\\" + fullPath[8..]);
                }
                else if (fullPath.Length >= 7 && char.IsAsciiLetter(fullPath[4])
                    && fullPath[5] == ':' && fullPath[6] == '\\')
                {
                    fullPath = Path.GetFullPath(fullPath[4..]);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported device namespace in file storage path.");
                }
            }
            else if (fullPath.StartsWith(@"\\.\", StringComparison.Ordinal)
                || fullPath.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported device namespace in file storage path.");
            }
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    internal static bool PathsEqual(string? first, string? second) => first is null || second is null
        ? first == second
        : string.Equals(GetComparisonPath(first), GetComparisonPath(second), StringComparison.OrdinalIgnoreCase);

    internal static bool IsSameOrDescendant(string candidatePath, string rootDirectory)
    {
        var candidate = GetComparisonPath(candidatePath);
        var root = GetComparisonPath(rootDirectory);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

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
        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(candidatePath);
        var comparisonRoot = GetComparisonPath(root);
        var comparisonCandidate = GetComparisonPath(candidate);
        var prefix = Path.EndsInDirectorySeparator(comparisonRoot)
            ? comparisonRoot : comparisonRoot + Path.DirectorySeparatorChar;

        if (string.Equals(comparisonRoot, comparisonCandidate, StringComparison.OrdinalIgnoreCase)
            || !comparisonCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
        var relative = Path.GetRelativePath(GetComparisonPath(root), GetComparisonPath(candidate));
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
