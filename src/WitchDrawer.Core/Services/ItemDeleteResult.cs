namespace WitchDrawer.Core.Services;

public sealed record ItemDeleteResult(
    Guid ItemId,
    string DisplayName,
    bool WasStoredItem,
    string? RestoredPath,
    bool RestoredToOriginal,
    bool RestoredToDesktop)
{
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

            if (RestoredToDesktop)
            {
                return $"已还原 {DisplayName} 到桌面（原位置不可用）";
            }

            return $"已还原 {DisplayName} 到原位置";
        }
    }
}
