using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.App.Infrastructure;

internal sealed class QuickPanelHotKeySettingsStore(ISettingsStore settings)
{
    internal const string SettingKey = "QuickPanelHotKey";

    public async Task<QuickPanelHotKey> LoadAsync(
        CancellationToken cancellationToken = default,
        StartupSettingsSnapshot? startupSnapshot = null)
    {
        var savedValue = startupSnapshot is not null
            ? startupSnapshot.Get(SettingKey)
            : await settings.GetSettingAsync(SettingKey, cancellationToken);
        return QuickPanelHotKey.TryParse(savedValue, out var hotKey)
            ? hotKey
            : QuickPanelHotKey.Default;
    }

    public Task SaveAsync(QuickPanelHotKey hotKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotKey);
        if (!hotKey.IsValid)
        {
            throw new ArgumentException("快捷键组合无效。", nameof(hotKey));
        }

        return settings.SetSettingAsync(SettingKey, hotKey.Serialize(), cancellationToken);
    }
}
