namespace WitchDrawer.Core.Abstractions;

public interface IDesktopIntegration
{
    Task<bool> IsStartupEnabledAsync();
    Task SetStartupEnabledAsync(bool enabled);
    Task<bool> AreDesktopIconsHiddenAsync();
    Task<bool> ToggleDesktopIconsAsync();
}
