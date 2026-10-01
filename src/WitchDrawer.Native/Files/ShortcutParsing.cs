namespace WitchDrawer.Native.Files;

/// <summary>
/// Pure parsing helpers for shortcut icon metadata, kept separate from the
/// P/Invoke-heavy <see cref="ShellIconExtractor"/> so they can be unit-tested
/// without touching the file system or Win32.
/// </summary>
public static class ShortcutParsing
{
    /// <summary>
    /// Parses a .url (Internet Shortcut) INI payload into an
    /// (<see cref="TargetUrl"/>, <see cref="IconLocation"/>) pair.
    /// </summary>
    /// <param name="content">The raw text of the .url file.</param>
    /// <param name="result">
    /// A tuple of (TargetUrl, IconLocation) where <c>IconLocation</c> is
    /// formatted as "<c>iconFile,index</c>" (or empty when the file declares none).
    /// </param>
    /// <returns><see langword="true"/> when at least one recognized field was found.</returns>
    public static bool TryParseUrlShortcut(string content, out (string TargetUrl, string IconLocation) result)
    {
        string? url = null;
        string? iconFile = null;
        var iconIndex = 0;
        var found = false;

        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '[' || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();

            if (key.Equals("URL", StringComparison.OrdinalIgnoreCase))
            {
                url = value.ToString();
                found = true;
            }
            else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)
                || key.Equals("IconPath", StringComparison.OrdinalIgnoreCase))
            {
                iconFile = value.ToString();
                found = true;
            }
            else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var parsedIndex))
            {
                iconIndex = parsedIndex;
                found = true;
            }
        }

        var target = url ?? string.Empty;
        var iconLocation = string.IsNullOrEmpty(iconFile) ? string.Empty : $"{iconFile},{iconIndex}";
        result = (target, iconLocation);
        return found;
    }

    /// <summary>
    /// Splits a Windows icon location string such as
    /// "<c>C:\Windows\System32\imageres.dll,109</c>" into its file path and
    /// icon index. A bare path (no index) resolves to index 0.
    /// </summary>
    public static bool TryParseIconLocation(string? iconLocation, out string file, out int iconIndex)
    {
        file = string.Empty;
        iconIndex = -1;

        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return false;
        }

        var value = iconLocation.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 0
            && int.TryParse(value[(commaIndex + 1)..].Trim(), out var parsedIndex))
        {
            // Trim quotes from the file segment after splitting, so a quoted
            // path with an index (e.g. "C:\My App\a.exe",0) keeps the closing
            // quote out of the file path.
            file = value[..commaIndex].Trim().Trim('"');
            iconIndex = parsedIndex;
        }
        else
        {
            // A bare icon path (.ico/.exe/.dll) refers to its first icon.
            file = value.Trim('"');
            iconIndex = 0;
        }

        return !string.IsNullOrWhiteSpace(file);
    }
}
