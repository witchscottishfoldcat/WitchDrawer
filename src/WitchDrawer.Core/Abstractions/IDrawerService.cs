using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Abstractions;

public interface IDrawerService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default);
    Task ReorderBoxesAsync(IReadOnlyList<Guid> orderedBoxIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid boxId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default);
    Task<Box> CreateBoxAsync(string name, BoxType type, CancellationToken cancellationToken = default);
    Task<DrawerItem> ImportPathAsync(Guid boxId, string sourcePath, int? gridColumn = null, int? gridRow = null, CancellationToken cancellationToken = default);
    Task UpdateItemGridPositionAsync(Guid itemId, int? gridColumn, int? gridRow, CancellationToken cancellationToken = default);
    Task UpdateItemGridPositionsAsync(IReadOnlyDictionary<Guid, (int GridColumn, int GridRow)> positions, CancellationToken cancellationToken = default);
    Task MoveItemToBoxAsync(Guid itemId, Guid targetBoxId, int? gridColumn = null, int? gridRow = null, CancellationToken cancellationToken = default);
    Task<string> ExportItemToDirectoryAsync(Guid itemId, string targetDirectory, CancellationToken cancellationToken = default);
    Task<ItemDeleteResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default);
    Task<BoxDeleteResult> DeleteBoxAsync(Guid boxId, CancellationToken cancellationToken = default);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default);
    Task RenameBoxAsync(Guid boxId, string newName, CancellationToken cancellationToken = default);
    Task OpenItemAsync(Guid itemId, IFileLauncher launcher, CancellationToken cancellationToken = default);
}
