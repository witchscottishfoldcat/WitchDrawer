namespace WitchDrawer.Core.Logging;

/// <summary>
/// 丢弃所有输出的 <see cref="IAppLogger"/>，用于日志器可选的构造参数默认值。
/// </summary>
public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    public void Info(string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}
