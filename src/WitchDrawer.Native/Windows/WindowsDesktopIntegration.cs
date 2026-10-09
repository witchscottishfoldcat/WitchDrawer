using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Windows;

public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    private readonly StartupRegistration _startup = new(
        Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable."),
        StartupRegistration.RunSubKey,
        [Environment.GetFolderPath(Environment.SpecialFolder.Startup),
         Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)]);
    public Task<bool> IsStartupEnabledAsync() => Task.Run(_startup.IsEnabled);
    public Task MigrateLegacyStartupShortcutsAsync() => Task.Run(_startup.MigrateLegacyShortcuts);
    public Task SetStartupEnabledAsync(bool enabled) => Task.Run(() => _startup.SetEnabled(enabled));
    public Task<bool> AreDesktopIconsHiddenAsync() => Task.Run(DesktopIconVisibility.IsHidden);
    public Task<bool> ToggleDesktopIconsAsync() => DesktopIconVisibility.ToggleHiddenAsync();

}
