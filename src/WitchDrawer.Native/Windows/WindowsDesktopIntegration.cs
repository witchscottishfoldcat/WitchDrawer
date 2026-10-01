using WitchDrawer.Core.Abstractions;

namespace WitchDrawer.Native.Windows;

public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    private const string RegistryValueName = "WitchDrawer";
    public Task<bool> IsStartupEnabledAsync() => Task.Run(ReadStartupRegistry);
    public Task SetStartupEnabledAsync(bool enabled) => Task.Run(() => WriteStartupRegistry(enabled));
    public Task<bool> AreDesktopIconsHiddenAsync() => Task.Run(DesktopIconVisibility.IsHidden);
    public Task<bool> ToggleDesktopIconsAsync() => DesktopIconVisibility.ToggleHiddenAsync();

    private static bool ReadStartupRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            var value = key?.GetValue(RegistryValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteStartupRegistry(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null)
        {
            return;
        }

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(RegistryValueName, $"\"{exePath}\" --silent");
            }
        }
        else
        {
            key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }
    }

}
