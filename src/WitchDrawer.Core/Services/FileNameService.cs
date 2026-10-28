namespace WitchDrawer.Core.Services;

internal static class FileNameService
{
    private const int MaximumComponentLength = 255;

    public static string GetUniqueDestinationPath(string directory, string originalName, bool isDirectory,
        ISet<string>? reservedTargets = null)
    {
        Directory.CreateDirectory(directory);

        var candidate = Path.Combine(directory, originalName);
        if (!Exists(candidate) && (reservedTargets is null || reservedTargets.Add(PathSafety.GetComparisonPath(candidate))))
        {
            return candidate;
        }

        var name = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(originalName);

        for (var index = 1; index < 10_000; index++)
        {
            var suffix = $" ({index})";
            // NTFS limits each filename to 255 UTF-16 code units, independently
            // of long-path support. Reserve room for the changing conflict suffix.
            var boundedExtension = TruncateWithoutSplittingSurrogate(extension,
                Math.Max(0, MaximumComponentLength - suffix.Length - 1));
            var boundedName = TruncateWithoutSplittingSurrogate(name,
                MaximumComponentLength - suffix.Length - boundedExtension.Length);
            candidate = Path.Combine(directory, boundedName + suffix + boundedExtension);
            if (!Exists(candidate) && (reservedTargets is null || reservedTargets.Add(PathSafety.GetComparisonPath(candidate))))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name for {originalName}.");
    }

    private static string TruncateWithoutSplittingSurrogate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
        return value[..length];
    }

    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}

