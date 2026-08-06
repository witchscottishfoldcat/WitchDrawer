using System.Text.Json;

namespace WitchDrawer.Core.Storage;

/// <summary>
/// 启动期数据目录引导配置。
/// 自定义数据根目录记录在默认目录（%LocalAppData%\WitchDrawer）下的小文件里，
/// 避免“配置存放在即将被迁移的数据库中”的自引用问题。
/// </summary>
public sealed class StorageLocationStore
{
    /// <summary>
    /// 引导配置文件名（固定位于默认数据根目录下）。
    /// </summary>
    public const string ConfigFileName = "storage-location.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public StorageLocationStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    /// <summary>
    /// 默认引导配置位置：%LocalAppData%\WitchDrawer\storage-location.json。
    /// </summary>
    public static StorageLocationStore ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "无法解析 LocalApplicationData，无法读取数据目录引导配置。");
        }

        return new StorageLocationStore(
            Path.Combine(localAppData, AppPaths.DefaultRootDirectoryName, ConfigFileName));
    }

    /// <summary>
    /// 读取用户配置的数据根目录；文件缺失或损坏时返回 null（回退默认目录）。
    /// </summary>
    public string? LoadConfiguredDirectory()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<StorageLocationConfig>(json);
            var directory = config?.DataDirectory;
            return string.IsNullOrWhiteSpace(directory)
                ? null
                : Path.GetFullPath(directory.Trim());
        }
        catch
        {
            // 配置损坏时回退默认目录，避免应用无法启动。
            return null;
        }
    }

    /// <summary>
    /// 保存用户配置的数据根目录。
    /// </summary>
    public void SaveConfiguredDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var config = new StorageLocationConfig(Path.GetFullPath(dataDirectory.Trim()));
        File.WriteAllText(_filePath, JsonSerializer.Serialize(config, SerializerOptions));
    }

    /// <summary>
    /// 清除自定义目录（恢复默认）。删除失败不影响调用方。
    /// </summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch
        {
            // 尽力清理。
        }
    }

    private sealed record StorageLocationConfig(string? DataDirectory);
}
