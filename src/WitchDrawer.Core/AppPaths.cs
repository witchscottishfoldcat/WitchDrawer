namespace WitchDrawer.Core;

/// <summary>
/// 应用本地数据路径约定。
/// 默认使用 %LocalAppData%\WitchDrawer，可通过环境变量覆盖。
/// </summary>
public sealed record AppPaths(string RootDirectory)
{
    /// <summary>
    /// 用于覆盖默认数据根目录的环境变量名。
    /// 例如：WITCHDRAWER_DATA_DIR=D:\data\WitchDrawer
    /// </summary>
    public const string DataDirectoryEnvironmentVariableName = "WITCHDRAWER_DATA_DIR";

    /// <summary>
    /// 默认数据目录名（位于 LocalApplicationData 下）。
    /// </summary>
    public const string DefaultRootDirectoryName = "WitchDrawer";

    /// <summary>
    /// 数据库文件名。
    /// </summary>
    public const string DatabaseFileName = "witchdrawer.db";

    /// <summary>
    /// 收纳盒实体文件根目录名。
    /// </summary>
    public const string BoxesDirectoryName = "Boxes";

    /// <summary>
    /// 日志目录名。
    /// </summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>
    /// 可写性探测文件名（创建后立即删除）。
    /// </summary>
    private const string WritabilityProbeFileName = ".witchdrawer_write_probe";

    public string BoxesDirectory => Path.Combine(RootDirectory, BoxesDirectoryName);

    public string DatabasePath => Path.Combine(RootDirectory, DatabaseFileName);

    public string LogsDirectory => Path.Combine(RootDirectory, LogsDirectoryName);

    /// <summary>
    /// 解析当前用户应使用的数据路径：
    /// 1. 环境变量 WITCHDRAWER_DATA_DIR（若设置且有效）
    /// 2. %LocalAppData%\WitchDrawer
    /// 解析后会校验目录可写；不可写时抛出带路径上下文的异常。
    /// </summary>
    public static AppPaths ForCurrentUser()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredPaths = new AppPaths(Path.GetFullPath(configuredRoot.Trim()));
            configuredPaths.EnsureCreatedAndWritable();
            return configuredPaths;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "无法解析 LocalApplicationData。请设置环境变量 "
                + DataDirectoryEnvironmentVariableName
                + " 指向可写目录。");
        }

        var defaultPaths = new AppPaths(Path.Combine(localAppData, DefaultRootDirectoryName));
        defaultPaths.EnsureCreatedAndWritable();
        return defaultPaths;
    }

    /// <summary>
    /// 创建必要目录结构（不做可写校验）。
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BoxesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// 创建必要目录，并验证根目录可创建/删除临时文件。
    /// SQLite 在 WAL 模式下需要在同目录创建 -wal/-shm，目录只读会导致 Error 14。
    /// </summary>
    public void EnsureCreatedAndWritable()
    {
        EnsureCreated();
        EnsureRootDirectoryWritable();
    }

    /// <summary>
    /// 探测根目录是否允许创建新文件。
    /// </summary>
    private void EnsureRootDirectoryWritable()
    {
        var probePath = Path.Combine(RootDirectory, WritabilityProbeFileName);
        try
        {
            using (var stream = new FileStream(
                       probePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
                stream.WriteByte(1);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "WitchDrawer 数据目录不可写，SQLite 无法创建数据库旁路文件（-wal/-shm）。"
                + Environment.NewLine
                + "数据目录: "
                + RootDirectory
                + Environment.NewLine
                + "请检查目录权限，或设置环境变量 "
                + DataDirectoryEnvironmentVariableName
                + " 指向可写目录。",
                exception);
        }
        finally
        {
            // DeleteOnClose 通常已清理；再兜底一次，避免探测文件残留。
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // 清理失败不影响启动判定；可写性已在创建阶段确认。
            }
        }
    }
}
