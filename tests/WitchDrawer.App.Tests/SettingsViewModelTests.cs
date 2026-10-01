using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SettingsPage_LoadsAndTogglesThroughInjectedDesktopAdapter()
    {
        var store = new MemorySettings();
        var desktop = new DesktopAdapter { StartupEnabled = true };
        var operations = new UiOperationState(NullAppLogger.Instance);
        var viewModel = new SettingsViewModel(store, NullAppLogger.Instance, desktop,
            new AutoHideSettingsStore(store), operations);

        await viewModel.LoadAsync();
        Assert.True(viewModel.LaunchOnStartup);
        Assert.False(viewModel.AreDesktopIconsHidden);
        await viewModel.ToggleLaunchOnStartupCommand.ExecuteAsync(null);
        Assert.False(desktop.StartupEnabled);
        Assert.False(viewModel.LaunchOnStartup);

        // An external desktop change happened after load. The adapter's result is authoritative.
        desktop.IconsHidden = true;
        await viewModel.ToggleDesktopIconsCommand.ExecuteAsync(null);
        Assert.False(viewModel.AreDesktopIconsHidden);
        Assert.Equal("已显示 Windows 桌面图标", operations.StatusText);
        await viewModel.ToggleDesktopDoubleClickCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDesktopDoubleClickEnabled);
        Assert.Equal(bool.TrueString, await store.GetSettingAsync(SettingsViewModel.DesktopDoubleClickSettingKey));
    }

    private sealed class DesktopAdapter : IDesktopIntegration
    {
        internal bool StartupEnabled { get; set; }
        internal bool IconsHidden { get; set; }
        public Task<bool> IsStartupEnabledAsync() => Task.FromResult(StartupEnabled);
        public Task SetStartupEnabledAsync(bool enabled) { StartupEnabled = enabled; return Task.CompletedTask; }
        public Task<bool> AreDesktopIconsHiddenAsync() => Task.FromResult(IconsHidden);
        public Task<bool> ToggleDesktopIconsAsync() { IconsHidden = !IconsHidden; return Task.FromResult(IconsHidden); }
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.GetValueOrDefault(key));
        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_values));
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        { _values[key] = value; return Task.CompletedTask; }
        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.Remove(key));
    }
}
