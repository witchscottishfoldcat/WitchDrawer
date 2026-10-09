using Microsoft.Win32;

namespace WitchDrawer.Native.Windows;

/// <summary>One Run entry, with migration of shortcuts created by older installers.</summary>
internal sealed class StartupRegistration(string executablePath, string registrySubKey, IEnumerable<string> startupFolders)
{
    internal const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "WitchDrawer";
    private readonly string[] _shortcutPaths = startupFolders
        .Where(folder => !string.IsNullOrWhiteSpace(folder))
        .Select(folder => Path.Combine(folder, "WitchDrawer.lnk"))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registrySubKey, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string) || GetOwnedShortcuts().Length > 0;
    }

    internal void MigrateLegacyShortcuts()
    {
        var ownedShortcuts = GetOwnedShortcuts();
        if (ownedShortcuts.Length == 0) return;
        using var key = Registry.CurrentUser.CreateSubKey(registrySubKey, writable: true);
        // Commit the replacement before deleting the old startup entry.
        key.SetValue(ValueName, $"\"{executablePath}\" --silent", RegistryValueKind.String);
        foreach (var path in ownedShortcuts) File.Delete(path);
    }

    internal void SetEnabled(bool enabled)
    {
        var ownedShortcuts = GetOwnedShortcuts();
        using var key = Registry.CurrentUser.CreateSubKey(registrySubKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\" --silent", RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);

        // Turning off must remove both mechanisms, including installations made by older versions.
        foreach (var path in ownedShortcuts) File.Delete(path);
    }

    private string[] GetOwnedShortcuts() => _shortcutPaths
        .Where(path => File.Exists(path) && StartupShortcutMigration.TargetsExecutable(path, executablePath))
        .ToArray();
}
