namespace WitchDrawer.Core.Services;

/// <summary>
/// Groups drawer item names/paths into display categories used by the
/// one-click classification feature. Each category owns a mapping-box name.
/// </summary>
public static class ClassifyCategory
{
    public sealed record Category(string BoxName, string DisplayName);

    public static Category GetCategory(string? displayName, string? sourcePath, bool isDirectory = false)
    {
        var extension = GetExtension(displayName, sourcePath);

        if (ImageExtensions.Contains(extension))
        {
            return new Category("图片收纳盒", "图片");
        }

        if (DocumentExtensions.Contains(extension))
        {
            return new Category("文档收纳盒", "文档");
        }

        if (VideoExtensions.Contains(extension))
        {
            return new Category("视频收纳盒", "视频");
        }

        if (AudioExtensions.Contains(extension))
        {
            return new Category("音频收纳盒", "音频");
        }

        if (ArchiveExtensions.Contains(extension))
        {
            return new Category("压缩包收纳盒", "压缩包");
        }

        if (isDirectory)
        {
            return new Category("文件夹收纳盒", "文件夹");
        }

        return new Category("其他收纳盒", "其他");
    }

    private static string GetExtension(string? displayName, string? sourcePath)
    {
        var name = displayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = sourcePath;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "svg", "tif", "tiff", "heic", "avif", "jfif"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "docx", "pdf", "txt", "md", "xls", "xlsx", "ppt", "pptx", "csv", "rtf", "odt", "ods", "odp", "wps", "et", "dps", "pages", "numbers", "key"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "rmvb", "rm", "3gp"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "flac", "aac", "ogg", "m4a", "wma", "opus", "ape", "amr"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "zst", "iso", "cab", "lz4"
    };
}
