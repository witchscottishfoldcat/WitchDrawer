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
    internal const string MigrationMarkerFileName = ".witchdrawer-migration";

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
            var intentPath = _filePath + ".migration";
            if (File.Exists(intentPath))
            {
                var intent = JsonSerializer.Deserialize<MigrationIntent>(File.ReadAllText(intentPath));
                if (intent is not null)
                {
                    var target = Path.GetFullPath(intent.TargetDirectory);
                    var marker = Path.Combine(target, MigrationMarkerFileName);
                    if (File.Exists(Path.Combine(target, AppPaths.DatabaseFileName))
                        && File.Exists(marker)
                        && string.Equals(File.ReadAllText(marker), intent.Id.ToString("N"), StringComparison.Ordinal))
                    {
                        try
                        {
                            SaveConfiguredDirectory(target);
                            ClearMigrationIntent(intent.Id);
                            TryDeleteMigrationMarker(target);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            // A confirmed promotion is authoritative even if repairing the
                            // bootstrap file is still blocked. Never reopen the old snapshot.
                        }
                        return target;
                    }
                }
            }
        }
        catch
        {
            // An incomplete or unreadable intent cannot override the last valid config.
        }

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

    internal void SaveMigrationIntent(string targetDirectory, Guid id)
    {
        var intentPath = _filePath + ".migration";
        var parent = Path.GetDirectoryName(intentPath)
            ?? throw new InvalidOperationException("迁移引导目录不可用。");
        Directory.CreateDirectory(parent);
        var temporaryPath = intentPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(new MigrationIntent(Path.GetFullPath(targetDirectory), id)));
        File.Move(temporaryPath, intentPath, overwrite: true);
    }

    internal void ClearMigrationIntent(Guid id)
    {
        var intentPath = _filePath + ".migration";
        try
        {
            if (File.Exists(intentPath))
            {
                var intent = JsonSerializer.Deserialize<MigrationIntent>(File.ReadAllText(intentPath));
                if (intent?.Id == id)
                {
                    File.Delete(intentPath);
                }
            }
        }
        catch
        {
            // A stale intent is safe once the main config points to the promoted target.
        }
    }

    internal static void TryDeleteMigrationMarker(string targetDirectory)
    {
        try
        {
            File.Delete(Path.Combine(targetDirectory, MigrationMarkerFileName));
        }
        catch
        {
        }
    }

    /// <summary>
    /// 保存用户配置的数据根目录。先写临时文件再原子替换：
    /// 写一半断电/崩溃不会留下损坏的 JSON（损坏配置会让启动静默回退默认目录，
    /// 用户自定义目录里的数据"看起来全丢了"）。
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
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// 清除自定义目录（恢复默认）。删除失败不影响调用方。
    /// </summary>
    public void Clear()
    {
        try
        {
            var intentPath = _filePath + ".migration";
            if (File.Exists(intentPath))
            {
                File.Delete(intentPath);
            }

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
    private sealed record MigrationIntent(string TargetDirectory, Guid Id);
}
