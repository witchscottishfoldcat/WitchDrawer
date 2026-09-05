using System.Globalization;
using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class ThemeOpacitySettingTests
{
    [Fact]
    public async Task UpgradeWithoutAppearanceSettings_PreservesCustomOpacityWithoutRounding()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();
        await workspace.DrawerService.SetSettingAsync(MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey, "2");
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            await workspace.DrawerService.SetSettingAsync(MainViewModel.GetThemeBoxOpacitySettingKey(theme), "0.68765");
        }

        try
        {
            await workspace.ViewModel.LoadAsync();
            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                Assert.Equal(0.68765, AppThemeManager.GetBoxOpacity(theme), 5);
                Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassStrokeBrush", 0.68765),
                    AppThemeManager.GetDesktopBoxBorderColor(theme));
                Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassInnerBrush", 0.68765),
                    AppThemeManager.GetDesktopIconFrameColor(theme));
                Assert.Null(await workspace.DrawerService.GetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme));
                Assert.Null(await workspace.DrawerService.GetSettingAsync(MainViewModel.IconFrameOpacitySettingKeyPrefix + theme));
            }
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task Load_RestoresIndependentAppearanceSettingsForEveryTheme()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();
        var values = new[] { (AppTheme.Moe, "0.70", "0.35"), (AppTheme.Glass, "0", "1"), (AppTheme.Crystal, "0.55", "0.15") };
        foreach (var (theme, border, frame) in values)
        {
            await workspace.DrawerService.SetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme, border);
            await workspace.DrawerService.SetSettingAsync(MainViewModel.IconFrameOpacitySettingKeyPrefix + theme, frame);
        }

        try
        {
            await workspace.ViewModel.LoadAsync();
            foreach (var (theme, border, frame) in values)
            {
                Assert.Equal(double.Parse(border, CultureInfo.InvariantCulture), AppThemeManager.GetBoxBorderOpacity(theme));
                Assert.Equal(double.Parse(frame, CultureInfo.InvariantCulture), AppThemeManager.GetIconFrameOpacity(theme));
            }
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-0.5")]
    [InlineData("2")]
    [InlineData("invalid")]
    public async Task InvalidAppearanceSettings_RetainOriginalAppearance(string value)
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();
        var theme = workspace.ViewModel.CurrentTheme;
        await workspace.DrawerService.SetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme, value);
        await workspace.DrawerService.SetSettingAsync(MainViewModel.IconFrameOpacitySettingKeyPrefix + theme, value);
        try
        {
            await workspace.ViewModel.LoadAsync();
            Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassStrokeBrush", AppThemeManager.GetBoxOpacity(theme)),
                AppThemeManager.GetDesktopBoxBorderColor(theme));
            Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassInnerBrush", AppThemeManager.GetBoxOpacity(theme)),
                AppThemeManager.GetDesktopIconFrameColor(theme));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task RapidChanges_SaveBothControlsAndReloadWithoutTouchingOtherThemes()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            await workspace.ViewModel.LoadAsync();
            var theme = workspace.ViewModel.CurrentTheme;
            var opacity = AppThemeManager.GetBoxOpacity(theme);
            for (var percent = 10; percent <= 75; percent++)
            {
                workspace.ViewModel.BoxBorderTransparencyPercent = percent;
                workspace.ViewModel.IconFrameTransparencyPercent = percent - 10;
            }

            workspace.ViewModel.BoxBorderTransparencyPercent = double.NaN;
            workspace.ViewModel.IconFrameTransparencyPercent = double.PositiveInfinity;
            await WaitForSettingAsync(workspace, MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme, "0.25");
            await WaitForSettingAsync(workspace, MainViewModel.IconFrameOpacitySettingKeyPrefix + theme, "0.35");
            AppThemeManager.ResetBoxOpacitiesForTests();
            await workspace.ViewModel.LoadAsync();
            Assert.Equal(75, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(65, workspace.ViewModel.IconFrameTransparencyPercent);
            Assert.Equal(opacity, AppThemeManager.GetBoxOpacity(theme));
            foreach (var other in Enum.GetValues<AppTheme>().Where(value => value != theme))
            {
                Assert.Null(await workspace.DrawerService.GetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + other));
                Assert.Null(await workspace.DrawerService.GetSettingAsync(MainViewModel.IconFrameOpacitySettingKeyPrefix + other));
            }
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task ResetDuringPendingSave_RestoresOnlyCurrentThemeAndKeepsEditorChoice()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();
        var theme = workspace.ViewModel.CurrentTheme;
        var other = Enum.GetValues<AppTheme>().First(value => value != theme);
        await workspace.DrawerService.SetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme, "0.15");
        await workspace.DrawerService.SetSettingAsync(MainViewModel.IconFrameOpacitySettingKeyPrefix + theme, "0.25");
        await workspace.DrawerService.SetSettingAsync(MainViewModel.BoxBorderOpacitySettingKeyPrefix + other, "0.55");
        await workspace.DrawerService.SetSettingAsync(MainViewModel.EditorFollowsBoxOpacitySettingKey, bool.TrueString);
        try
        {
            await workspace.ViewModel.LoadAsync();
            workspace.ViewModel.ThemeTransparencyPercent = 42;
            workspace.ViewModel.BoxBorderTransparencyPercent = 95;
            workspace.ViewModel.IconFrameTransparencyPercent = 65;
            workspace.ViewModel.ResetThemeTransparencyCommand.Execute(null);
            await WaitForSettingAsync(workspace, MainViewModel.BoxBorderOpacitySettingKeyPrefix + theme, null);
            await WaitForSettingAsync(workspace, MainViewModel.IconFrameOpacitySettingKeyPrefix + theme, null);
            await WaitForSettingAsync(workspace, MainViewModel.GetThemeBoxOpacitySettingKey(theme),
                FormatOpacity(AppThemeManager.GetDefaultBoxOpacity(theme)));
            AppThemeManager.ResetBoxOpacitiesForTests();
            await workspace.ViewModel.LoadAsync();
            Assert.Equal(AppThemeManager.GetDefaultBoxOpacity(theme), AppThemeManager.GetBoxOpacity(theme));
            Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassStrokeBrush", AppThemeManager.GetBoxOpacity(theme)),
                AppThemeManager.GetDesktopBoxBorderColor(theme));
            Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "GlassInnerBrush", AppThemeManager.GetBoxOpacity(theme)),
                AppThemeManager.GetDesktopIconFrameColor(theme));
            Assert.Equal(0.55, AppThemeManager.GetBoxBorderOpacity(other));
            Assert.True(workspace.ViewModel.EditorFollowsBoxOpacity);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    private static async Task WaitForSettingAsync(ThemeWorkspace workspace, string key, string? expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await workspace.DrawerService.GetSettingAsync(key) == expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Equal(expected, await workspace.DrawerService.GetSettingAsync(key));
    }

    [Fact]
    public async Task FirstLaunch_UsesThemeSpecificDefaultOpacities()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests(AppThemeManager.MaximumBoxOpacity);

        try
        {
            await workspace.ViewModel.LoadAsync();

            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                Assert.Equal(
                    AppThemeManager.GetDefaultBoxOpacity(theme),
                    AppThemeManager.GetBoxOpacity(theme),
                    3);
                Assert.Equal(
                    FormatOpacity(AppThemeManager.GetDefaultBoxOpacity(theme)),
                    await workspace.DrawerService.GetSettingAsync(
                        MainViewModel.GetThemeBoxOpacitySettingKey(theme)));
            }

            Assert.Equal(0, workspace.ViewModel.ThemeTransparencyPercent);
            Assert.Equal("0%", workspace.ViewModel.ThemeTransparencyLabel);
            Assert.Equal(
                "2",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task Upgrade_PreservesOldThemeOpacityAndMigratesTransparentCrystalFlag()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.AboutPageShownSettingKey,
            bool.TrueString);
        await workspace.DrawerService.SetSettingAsync("Theme", AppTheme.Glass.ToString());
        await workspace.DrawerService.SetSettingAsync("CrystalBoxTransparency", bool.TrueString);
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            Assert.Equal(
                AppThemeManager.GetLegacyBoxOpacity(AppTheme.Moe),
                AppThemeManager.GetBoxOpacity(AppTheme.Moe),
                3);
            Assert.Equal(
                AppThemeManager.GetLegacyBoxOpacity(AppTheme.Glass),
                AppThemeManager.GetBoxOpacity(AppTheme.Glass),
                3);
            Assert.Equal(
                AppThemeManager.DefaultBoxOpacity,
                AppThemeManager.GetBoxOpacity(AppTheme.Crystal),
                3);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task InstallWithoutOldThemeSettings_UsesThemeSpecificDefaults()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                Assert.Equal(
                    AppThemeManager.GetDefaultBoxOpacity(theme),
                    AppThemeManager.GetBoxOpacity(theme),
                    3);
            }
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task Load_RestoresIndependentSavedOpacityForEveryTheme()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey,
            "2");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Moe),
            "0.25");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Glass),
            "0.55");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Crystal),
            "0.85");
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            Assert.Equal(0.25, AppThemeManager.GetBoxOpacity(AppTheme.Moe), 3);
            Assert.Equal(0.55, AppThemeManager.GetBoxOpacity(AppTheme.Glass), 3);
            Assert.Equal(0.85, AppThemeManager.GetBoxOpacity(AppTheme.Crystal), 3);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task UpgradeFromVersionOne_CorrectsGeneratedDefaultsAndKeepsCustomValues()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey,
            "1");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Moe),
            "0.25");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Glass),
            "1.00");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Crystal),
            "1.00");
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            Assert.Equal(0.25, AppThemeManager.GetBoxOpacity(AppTheme.Moe), 3);
            Assert.Equal(0.82, AppThemeManager.GetBoxOpacity(AppTheme.Glass), 3);
            Assert.Equal(0.40, AppThemeManager.GetBoxOpacity(AppTheme.Crystal), 3);
            Assert.Equal(
                "2",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task ChangingSlider_AppliesAndPersistsCurrentThemeOpacity()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            workspace.ViewModel.ThemeTransparencyPercent = 35;

            var settingKey = MainViewModel.GetThemeBoxOpacitySettingKey(
                AppThemeManager.CurrentTheme);
            string? savedOpacity = null;
            for (var attempt = 0; attempt < 40 && savedOpacity != "0.65"; attempt++)
            {
                await Task.Delay(25);
                savedOpacity = await workspace.DrawerService.GetSettingAsync(settingKey);
            }

            Assert.Equal(0.65, AppThemeManager.GetBoxOpacity(AppThemeManager.CurrentTheme), 3);
            Assert.Equal("0.65", savedOpacity);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task EditorOpacityFollow_DefaultsOffAndTogglePersistsTheChoice()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();

        await workspace.ViewModel.LoadAsync();

        Assert.False(workspace.ViewModel.EditorFollowsBoxOpacity);

        await workspace.ViewModel.ToggleEditorOpacityFollowCommand.ExecuteAsync(null);

        Assert.True(workspace.ViewModel.EditorFollowsBoxOpacity);
        Assert.Equal(
            bool.TrueString,
            await workspace.DrawerService.GetSettingAsync(
                MainViewModel.EditorFollowsBoxOpacitySettingKey));
    }

    [Fact]
    public async Task EditorOpacityFollow_LoadsTheSavedChoice()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.EditorFollowsBoxOpacitySettingKey,
            bool.TrueString);

        await workspace.ViewModel.LoadAsync();

        Assert.True(workspace.ViewModel.EditorFollowsBoxOpacity);
    }

    [Fact]
    public async Task TransparencyPercent_RejectsNonFiniteDirectInput()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();
            workspace.ViewModel.ThemeTransparencyPercent = 35;

            workspace.ViewModel.ThemeTransparencyPercent = double.NaN;
            workspace.ViewModel.ThemeTransparencyPercent = double.PositiveInfinity;

            Assert.Equal(35, workspace.ViewModel.ThemeTransparencyPercent);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    private static string FormatOpacity(double opacity)
    {
        return opacity.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private sealed class ThemeWorkspace : IAsyncDisposable
    {
        private ThemeWorkspace(
            string root,
            DrawerService drawerService,
            MainViewModel viewModel)
        {
            Root = root;
            DrawerService = drawerService;
            ViewModel = viewModel;
        }

        public string Root { get; }

        public DrawerService DrawerService { get; }

        public MainViewModel ViewModel { get; }

        public static async Task<ThemeWorkspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "WitchDrawerTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = new RecordingLogger();
            var launcher = new NoOpFileLauncher();
            var visualStyleStore = new BoxVisualStyleStore(drawerService, logger);
            var viewModel = new MainViewModel(
                drawerService,
                new TodoService(repository),
                launcher,
                logger,
                new QuickPanelViewModel(drawerService, launcher, logger, visualStyleStore),
                new UpdateService(logger),
                visualStyleStore,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))));

            return new ThemeWorkspace(root, drawerService, viewModel);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
    }
}
