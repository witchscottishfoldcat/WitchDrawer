using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core;
using WitchDrawer.Native.HotKeys;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Tests;

public sealed class QuickPanelHotKeyTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsConfiguredCombination()
    {
        var original = new QuickPanelHotKey(
            HotKeyModifiers.Control | HotKeyModifiers.Shift,
            0x4B);

        var parsed = QuickPanelHotKey.TryParse(original.Serialize(), out var result);

        Assert.True(parsed);
        Assert.Equal(original, result);
        Assert.Equal("Ctrl + Shift + K", result.DisplayText);
        Assert.Equal(
            HotKeyModifiers.Control | HotKeyModifiers.Shift | HotKeyModifiers.NoRepeat,
            result.RegistrationModifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hotkey")]
    [InlineData("4:41")]
    [InlineData("2:0")]
    [InlineData("4002:57")]
    public void TryParse_RejectsUnsafeOrMalformedValues(string? value)
    {
        Assert.False(QuickPanelHotKey.TryParse(value, out var result));
        Assert.Equal(QuickPanelHotKey.Default, result);
    }

    [Fact]
    public async Task SettingsStore_PersistsConfiguredHotKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            await drawerService.InitializeAsync();
            var store = new QuickPanelHotKeySettingsStore(drawerService);
            var configured = new QuickPanelHotKey(
                HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
                0x51);

            Assert.Equal(QuickPanelHotKey.Default, await store.LoadAsync());

            await store.SaveAsync(configured);

            Assert.Equal(configured, await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                TestCleanup.DeleteDirectory(root);
            }
        }
    }

    [Fact]
    public async Task StartupInitialization_OnFreshDataDirectory_CreatesSchemaBeforeLoadingHotKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            var store = new QuickPanelHotKeySettingsStore(drawerService);

            var hotKey = await global::WitchDrawer.App.App.InitializeDataAndLoadQuickPanelHotKeyAsync(
                drawerService,
                store);

            Assert.Equal(QuickPanelHotKey.Default, hotKey);
            Assert.True(File.Exists(paths.DatabasePath));
            Assert.NotEmpty(await drawerService.GetBoxesAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                TestCleanup.DeleteDirectory(root);
            }
        }
    }
}
