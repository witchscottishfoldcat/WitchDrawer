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
                // 带出首条失败明细（项目名 + 原因），用户反馈时可直接定位，
                // 不再只有"N 项还原失败"这种无法排查的计数。
                var detail = FailedCount > 0 && Failures.Count > 0
                    ? $"（{Failures[0]}）"
                    : string.Empty;
                return FailedCount > 0
                    ? $"删除未完成：{FailedCount} 项还原失败{detail}，收纳盒已保留"
                    : $"删除未完成，收纳盒已保留";
            }

            if (BoxType == BoxType.Mapping)
            {
                return $"已删除 {BoxName}，引用已移除";
            }

            if (BoxType == BoxType.Todo)
            {
                return $"已删除 {BoxName}，待办事项已清除";
            }

            if (RestoredCount <= 0)
            {
                return $"已删除 {BoxName}";
            }

            return $"已删除 {BoxName}，已还原 {RestoredCount} 项";
        }
    }
}
