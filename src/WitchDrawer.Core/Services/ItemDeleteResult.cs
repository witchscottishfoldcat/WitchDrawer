using WitchDrawer.Core.Localization;
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
                return Strings.Format("RemovedReference", DisplayName);
            }

            if (RemovedMissingRecord)
            {
                return Strings.Format("TheFileNoLongerExistsRemovedItsBoxEntry", DisplayName);
            }

            if (RestoredToDesktop)
            {
                return Strings.Format("RestoredToTheDesktopOriginalLocationUnavailable", DisplayName);
            }

            return Strings.Format("RestoredToItsOriginalLocation", DisplayName);
        }
    }
}
