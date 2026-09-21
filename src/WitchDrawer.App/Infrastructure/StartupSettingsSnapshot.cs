namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// 本轮启动的设置快照：启动时通过一次数据库查询生成，
/// 主窗口与桌面盒子共享，避免逐盒逐项重复打开连接读取。
/// 快照仅用于本轮启动的初始化；运行期间修改设置仍走正常持久化路径，
/// 之后再读取时应查询数据库而不是使用本快照，避免读到过期值。
/// </summary>
public sealed class StartupSettingsSnapshot
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public StartupSettingsSnapshot(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    public static StartupSettingsSnapshot Empty { get; } =
        new(new Dictionary<string, string>());

    public int Count => _values.Count;

    /// <summary>读取设置值；键不存在时返回 null（与数据库读取语义一致）。</summary>
    public string? Get(string key) =>
        _values.TryGetValue(key, out var value) ? value : null;
}
