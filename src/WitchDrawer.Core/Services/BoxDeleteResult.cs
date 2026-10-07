using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

public sealed record BoxDeleteResult(
    Guid BoxId,
    string BoxName,
    BoxType BoxType,
    bool BoxRemoved,
    int RestoredCount,
    int FailedCount,
    IReadOnlyList<string> Failures,
    int MissingCount = 0)
{
    public string StatusMessage
    {
        get
        {
            var missingText = MissingCount > 0 ? Strings.Format("RemovedMissingEntries", MissingCount) : string.Empty;
            if (!BoxRemoved)
            {
                // 带出首条失败明细（项目名 + 原因），用户反馈时可直接定位，
                // 不再只有"N 项还原失败"这种无法排查的计数。
                var detail = FailedCount > 0 && Failures.Count > 0
                    ? Strings.Format("Message", Failures[0])
                    : string.Empty;
                return FailedCount > 0
                    ? Strings.Format("DeletionIncompleteItemsCouldNotBeRestoredTheBox", FailedCount, detail, missingText)
                    : Strings.Format("DeletionIncompleteTheBoxWasKept", missingText);
            }

            if (BoxType == BoxType.Mapping)
            {
                return Strings.Format("DeletedReferencesRemoved", BoxName);
            }

            if (BoxType == BoxType.Todo)
            {
                return Strings.Format("DeletedTasksCleared", BoxName);
            }

            if (RestoredCount <= 0)
            {
                return Strings.Format("Deleted", BoxName, missingText);
            }

            return Strings.Format("DeletedRestoredItems", BoxName, RestoredCount, missingText);
        }
    }
}
