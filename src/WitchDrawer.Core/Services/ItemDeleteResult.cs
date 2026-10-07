namespace WitchDrawer.Core.Services;

public sealed record ItemDeleteResult(
    Guid ItemId,
    string DisplayName,
    bool WasStoredItem,
    string? RestoredPath,
    bool RestoredToOriginal,
    bool RestoredToDesktop)
{
    public bool RemovedMissingRecord => WasStoredItem && RestoredPath is null;

    public static ItemDeleteResult MissingRecordRemoved(Guid itemId, string displayName)
        => new(itemId, displayName, WasStoredItem: true, RestoredPath: null,
            RestoredToOriginal: false, RestoredToDesktop: false);

    public static ItemDeleteResult ReferenceRemoved(Guid itemId, string displayName)
    {
        return new ItemDeleteResult(
            itemId,
            displayName,
            WasStoredItem: false,
            RestoredPath: null,
            RestoredToOriginal: false,
            RestoredToDesktop: false);
    }

    public string StatusMessage
    {
        get
        {
            if (!WasStoredItem)
            {
                return $"已移除引用 {DisplayName}";
            }

            if (RemovedMissingRecord)
            {
                return $"文件已不存在，已移除 {DisplayName} 的收纳记录";
            }

            if (RestoredToDesktop)
            {
                return $"已还原 {DisplayName} 到桌面（原位置不可用）";
            }

            return $"已还原 {DisplayName} 到原位置";
        }
    }
}
