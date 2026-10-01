namespace WitchDrawer.Core.Abstractions;

public interface ISettingsStore
{
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default);
}
