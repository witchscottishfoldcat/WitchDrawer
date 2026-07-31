using System.Collections.Concurrent;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed class BoxPositionLockStateStore(
    DrawerService drawerService,
    IAppLogger logger)
{
    private const string SettingKeyPrefix = "BoxPositionLocked:";
    private readonly ConcurrentDictionary<Guid, bool> _cache = new();

    public async Task<bool> LoadAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(boxId, out var cachedState))
        {
            return cachedState;
        }

        string? savedValue;
        try
        {
            savedValue = await drawerService.GetSettingAsync(
                GetSettingKey(boxId),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                $"Failed to load position lock state for box {boxId:N}.");
            throw;
        }

        if (string.IsNullOrWhiteSpace(savedValue))
        {
            _cache[boxId] = false;
            return false;
        }

        if (bool.TryParse(savedValue, out var isPositionLocked))
        {
            _cache[boxId] = isPositionLocked;
            return isPositionLocked;
        }

        logger.Error(
            new FormatException($"Unsupported position lock state value: {savedValue}"),
            $"Ignored invalid position lock state for box {boxId:N}; using false.");
        _cache[boxId] = false;
        return false;
    }

    public async Task SaveAsync(
        Guid boxId,
        bool isPositionLocked,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await drawerService.SetSettingAsync(
                GetSettingKey(boxId),
                isPositionLocked.ToString(),
                cancellationToken);
            _cache[boxId] = isPositionLocked;
            logger.Info(
                $"Saved position lock state {isPositionLocked} for box {boxId:N}.");
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                $"Failed to save position lock state {isPositionLocked} "
                + $"for box {boxId:N}.");
            throw;
        }
    }

    internal static string GetSettingKey(Guid boxId)
    {
        return SettingKeyPrefix + boxId.ToString("N");
    }
}
