using System.Collections.Concurrent;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed class BoxVisualStyleStore(
    DrawerService drawerService,
    IAppLogger logger)
{
    private const string SettingKeyPrefix = "BoxVisualStyle:";
    private readonly ConcurrentDictionary<Guid, BoxVisualStyle> _cache = new();

    public async Task<BoxVisualStyle> LoadAsync(
        Box box,
        CancellationToken cancellationToken = default)
    {
        if (box.Type is not BoxType.Normal and not BoxType.Pixel)
        {
            _cache[box.Id] = BoxVisualStyle.Modern;
            return BoxVisualStyle.Modern;
        }

        if (_cache.TryGetValue(box.Id, out var cachedStyle))
        {
            return cachedStyle;
        }

        var fallback = GetLegacyCompatibleFallback(box);
        string? savedValue;
        try
        {
            savedValue = await drawerService.GetSettingAsync(
                GetSettingKey(box.Id),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                $"Failed to load visual style for box {box.Id:N}.");
            throw;
        }

        if (string.IsNullOrWhiteSpace(savedValue))
        {
            _cache[box.Id] = fallback;
            return fallback;
        }

        if (Enum.TryParse<BoxVisualStyle>(savedValue, ignoreCase: true, out var parsedStyle)
            && BoxVisualStyleCatalog.IsSupported(parsedStyle))
        {
            _cache[box.Id] = parsedStyle;
            return parsedStyle;
        }

        logger.Error(
            new FormatException($"Unsupported box visual style value: {savedValue}"),
            $"Ignored invalid visual style for box {box.Id:N}; using {fallback}.");
        _cache[box.Id] = fallback;
        return fallback;
    }

    public async Task SaveAsync(
        Guid boxId,
        BoxVisualStyle style,
        CancellationToken cancellationToken = default)
    {
        if (!BoxVisualStyleCatalog.IsSupported(style))
        {
            throw new ArgumentOutOfRangeException(
                nameof(style),
                style,
                "Unsupported box visual style.");
        }

        try
        {
            await drawerService.SetSettingAsync(
                GetSettingKey(boxId),
                style.ToString(),
                cancellationToken);
            _cache[boxId] = style;
            logger.Info($"Saved visual style {style} for box {boxId:N}.");
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                $"Failed to save visual style {style} for box {boxId:N}.");
            throw;
        }
    }

    internal static string GetSettingKey(Guid boxId)
    {
        return SettingKeyPrefix + boxId.ToString("N");
    }

    private static BoxVisualStyle GetLegacyCompatibleFallback(Box box)
    {
        return box.Type == BoxType.Pixel
            ? BoxVisualStyle.Pixel
            : BoxVisualStyle.Modern;
    }
}
