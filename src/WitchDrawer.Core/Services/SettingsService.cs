using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Services;

/// <summary>Owns background settings I/O and ordered writes independently of file operations.</summary>
public sealed class SettingsService : ISettingsStore
{
    private readonly SettingsRepository _repository;
    private readonly SemaphoreSlim _settingsWriteGate = new(1, 1);

    public SettingsService(DrawerRepository repository) => _repository = repository.Settings;

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _repository.GetSettingAsync(key, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 启动时一次性读取全部设置（单连接单查询），供主窗口与桌面盒子共享启动快照。
    /// 快照仅用于本轮启动；运行期间的读取仍走 <see cref="GetSettingAsync"/>。
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        CancellationToken cancellationToken = default)
        => Task.Run(() => _repository.GetAllSettingsAsync(cancellationToken), cancellationToken);

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        // Queue before dispatching to the pool so rapid UI changes cannot persist
        // an older value after a newer one. Waiting for SQLite never blocks WPF.
        await _settingsWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => _repository.SetSettingAsync(key, value, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _settingsWriteGate.Release();
        }
    }

    public async Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await _settingsWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => _repository.DeleteSettingAsync(key, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _settingsWriteGate.Release();
        }
    }

}
