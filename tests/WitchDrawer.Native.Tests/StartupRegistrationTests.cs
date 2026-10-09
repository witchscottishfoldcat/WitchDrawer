using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.Native.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void RegistryStartup_EnableDisableUsesOneSilentEntry()
    {
        using var fixture = new Fixture();
        Assert.False(fixture.Registration.IsEnabled());
        fixture.Registration.SetEnabled(true);
        Assert.True(fixture.Registration.IsEnabled());
        Assert.Equal($"\"{fixture.Executable}\" --silent", fixture.ReadCommand());
        fixture.Registration.SetEnabled(false);
        Assert.False(fixture.Registration.IsEnabled());
        Assert.Null(fixture.ReadCommand());
    }

    [Fact]
    public void LegacyShortcut_IsVisibleInSettingsMigratedOnceAndCanBeDisabled()
    {
        using var fixture = new Fixture();
        fixture.CreateShortcut(fixture.Executable);
        Assert.True(fixture.Registration.IsEnabled());
        Assert.Null(fixture.ReadCommand()); // Reading the setting does not write to the registry.
        fixture.Registration.MigrateLegacyShortcuts();
        Assert.False(File.Exists(fixture.Shortcut));
        Assert.Equal($"\"{fixture.Executable}\" --silent", fixture.ReadCommand());
        fixture.Registration.MigrateLegacyShortcuts();
        fixture.Registration.SetEnabled(false);
        Assert.False(fixture.Registration.IsEnabled());
    }

    [Fact]
    public void DisableWithoutMigration_RemovesBothOldAndNewStartupEntries()
    {
        using var fixture = new Fixture();
        fixture.Registration.SetEnabled(true);
        fixture.CreateShortcut(fixture.Executable);
        fixture.Registration.SetEnabled(false);
        Assert.False(File.Exists(fixture.Shortcut));
        Assert.Null(fixture.ReadCommand());
    }

    [Fact]
    public void UnrelatedShortcutWithSameName_IsPreserved()
    {
        using var fixture = new Fixture();
        fixture.CreateShortcut(Path.Combine(fixture.Root, "other.exe"));
        var contents = File.ReadAllBytes(fixture.Shortcut);
        Assert.False(fixture.Registration.IsEnabled());
        fixture.Registration.MigrateLegacyShortcuts();
        fixture.Registration.SetEnabled(true);
        fixture.Registration.SetEnabled(false);
        Assert.Equal(contents, File.ReadAllBytes(fixture.Shortcut));
    }

    [Fact]
    public void InvalidLegacyShortcut_DoesNotPreventReadingOrChangingTheRunSetting()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Shortcut, "invalid shortcut; preserve it");
        Assert.False(fixture.Registration.IsEnabled());
        fixture.Registration.MigrateLegacyShortcuts();
        fixture.Registration.SetEnabled(true);
        Assert.True(fixture.Registration.IsEnabled());
        fixture.Registration.SetEnabled(false);
        Assert.False(fixture.Registration.IsEnabled());
        Assert.Equal("invalid shortcut; preserve it", File.ReadAllText(fixture.Shortcut));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "WitchDrawer.StartupTests", Guid.NewGuid().ToString("N"));
        private readonly string _registryPath = @"Software\WitchDrawer.Tests\" + Guid.NewGuid().ToString("N");
        internal string Executable => Path.Combine(Root, "WitchDrawer.App.exe");
        internal string Shortcut => Path.Combine(Root, "WitchDrawer.lnk");
        internal StartupRegistration Registration { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Registration = new StartupRegistration(Executable, _registryPath, [Root]);
        }

        internal string? ReadCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            return key?.GetValue(StartupRegistration.ValueName) as string;
        }

        internal void CreateShortcut(string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell")!;
            var shell = Activator.CreateInstance(type)!;
            object? shortcut = null;
            try
            {
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [Shortcut])!;
                shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [target]);
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false);
            Directory.Delete(Root, recursive: true);
        }
    }
}
