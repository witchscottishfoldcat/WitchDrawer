using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

public sealed record BoxDeleteResult(
    Guid BoxId,
    string BoxName,
    BoxType BoxType,
    bool BoxRemoved,
    int RestoredCount,
    int FailedCount,
    IReadOnlyList<string> Failures)
{
    public string StatusMessage
    {
        get
        {
            if (!BoxRemoved)
            {
                return FailedCount > 0
                    ? $"删除未完成：{FailedCount} 项还原失败，收纳盒已保留"
                    : $"删除未完成，收纳盒已保留";
            }

            if (BoxType == BoxType.Mapping)
            {
                return $"已删除 {BoxName}，引用已移除";
            }

            if (RestoredCount <= 0)
            {
                return $"已删除 {BoxName}";
            }

            return $"已删除 {BoxName}，已还原 {RestoredCount} 项";
        }
    }
}
