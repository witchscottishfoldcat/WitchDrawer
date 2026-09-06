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
                Assert.Equal(
                    FormatOpacity(AppThemeManager.GetBoxBorderOpacity(theme)),
                    await workspace.DrawerService.GetSettingAsync(
                        MainViewModel.GetThemeBoxBorderOpacitySettingKey(theme)));
                Assert.Equal(
                    FormatOpacity(AppThemeManager.GetIconFrameOpacity(theme)),
                    await workspace.DrawerService.GetSettingAsync(
                        MainViewModel.GetThemeIconFrameOpacitySettingKey(theme)));
            }

            Assert.Equal(0, workspace.ViewModel.ThemeTransparencyPercent);
            Assert.Equal("0%", workspace.ViewModel.ThemeTransparencyLabel);
            Assert.Equal(0, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(0, workspace.ViewModel.IconFrameTransparencyPercent);
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
    public async Task Load_RestoresIndependentDesktopChromeOpacityForCurrentTheme()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxBorderOpacitySettingKey(AppTheme.Moe),
            "0.35");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeIconFrameOpacitySettingKey(AppTheme.Moe),
            "0.70");
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            Assert.Equal(0.35, AppThemeManager.GetBoxBorderOpacity(AppTheme.Moe), 3);
            Assert.Equal(0.70, AppThemeManager.GetIconFrameOpacity(AppTheme.Moe), 3);
            Assert.Equal(65, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(30, workspace.ViewModel.IconFrameTransparencyPercent);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task UpgradeWithoutChromeSettings_PreservesLegacyBorderOpacityForCustomBoxOpacity()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.ThemeBoxOpacityMigrationVersionSettingKey,
            "2");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Moe),
            "0.40");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Glass),
            "0.40");
        await workspace.DrawerService.SetSettingAsync(
            MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Crystal),
            "0.10");
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            Assert.Equal(1.00, AppThemeManager.GetBoxBorderOpacity(AppTheme.Moe), 3);
            Assert.Equal(0.20, AppThemeManager.GetBoxBorderOpacity(AppTheme.Glass), 3);
            Assert.Equal(0.40, AppThemeManager.GetBoxBorderOpacity(AppTheme.Crystal), 3);
            Assert.Equal(
                "1.00",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxBorderOpacitySettingKey(AppTheme.Moe)));
            Assert.Equal(
                "0.20",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxBorderOpacitySettingKey(AppTheme.Glass)));
            Assert.Equal(
                "0.40",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxBorderOpacitySettingKey(AppTheme.Crystal)));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task ChangingDesktopChromeSliders_AppliesAndPersistsFullRange()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            workspace.ViewModel.BoxBorderTransparencyPercent = 100;
            workspace.ViewModel.IconFrameTransparencyPercent = 37;

            var borderSettingKey = MainViewModel.GetThemeBoxBorderOpacitySettingKey(
                AppThemeManager.CurrentTheme);
            var iconFrameSettingKey = MainViewModel.GetThemeIconFrameOpacitySettingKey(
                AppThemeManager.CurrentTheme);
            string? savedBorderOpacity = null;
            string? savedIconFrameOpacity = null;
            for (var attempt = 0;
                 attempt < 40 && (savedBorderOpacity != "0.00" || savedIconFrameOpacity != "0.63");
                 attempt++)
            {
                await Task.Delay(25);
                savedBorderOpacity = await workspace.DrawerService.GetSettingAsync(borderSettingKey);
                savedIconFrameOpacity = await workspace.DrawerService.GetSettingAsync(iconFrameSettingKey);
            }

            Assert.Equal(0, AppThemeManager.GetBoxBorderOpacity(AppThemeManager.CurrentTheme));
            Assert.Equal(0.63, AppThemeManager.GetIconFrameOpacity(AppThemeManager.CurrentTheme), 3);
            Assert.Equal("0.00", savedBorderOpacity);
            Assert.Equal("0.63", savedIconFrameOpacity);
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task ResetThemeTransparency_RestoresAndPersistsCurrentThemeDefaults()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            workspace.ViewModel.ThemeTransparencyPercent = 60;
            workspace.ViewModel.BoxBorderTransparencyPercent = 80;
            workspace.ViewModel.IconFrameTransparencyPercent = 90;

            workspace.ViewModel.ResetThemeTransparencyCommand.Execute(null);

            Assert.Equal(0, workspace.ViewModel.ThemeTransparencyPercent);
            Assert.Equal(0, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(0, workspace.ViewModel.IconFrameTransparencyPercent);
            Assert.Equal(1, AppThemeManager.GetBoxOpacity(AppTheme.Moe));
            Assert.Equal(1, AppThemeManager.GetBoxBorderOpacity(AppTheme.Moe));
            Assert.Equal(1, AppThemeManager.GetIconFrameOpacity(AppTheme.Moe));
            Assert.Equal("已恢复 清透雅致 的透明度默认值", workspace.ViewModel.StatusText);

            await workspace.ViewModel.FlushPendingOpacitySavesAsync();

            Assert.Equal(
                "1.00",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxOpacitySettingKey(AppTheme.Moe)));
            Assert.Equal(
                "1.00",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxBorderOpacitySettingKey(AppTheme.Moe)));
            Assert.Equal(
                "1.00",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeIconFrameOpacitySettingKey(AppTheme.Moe)));
        }
        finally
        {
            AppThemeManager.ResetBoxOpacitiesForTests();
        }
    }

    [Fact]
    public async Task FlushPendingOpacitySaves_PersistsLatestValuesWithoutWaitingForDebounce()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            workspace.ViewModel.ThemeTransparencyPercent = 12;
            workspace.ViewModel.ThemeTransparencyPercent = 33;
            workspace.ViewModel.ThemeTransparencyPercent = 41;
            workspace.ViewModel.BoxBorderTransparencyPercent = 24;
            workspace.ViewModel.BoxBorderTransparencyPercent = 51;
            workspace.ViewModel.BoxBorderTransparencyPercent = 72;
            workspace.ViewModel.IconFrameTransparencyPercent = 60;
            workspace.ViewModel.IconFrameTransparencyPercent = 39;
            workspace.ViewModel.IconFrameTransparencyPercent = 18;

            await workspace.ViewModel.FlushPendingOpacitySavesAsync();

            var theme = AppThemeManager.CurrentTheme;
            Assert.Equal(
                "0.59",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxOpacitySettingKey(theme)));
            Assert.Equal(
                "0.28",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeBoxBorderOpacitySettingKey(theme)));
            Assert.Equal(
                "0.82",
                await workspace.DrawerService.GetSettingAsync(
                    MainViewModel.GetThemeIconFrameOpacitySettingKey(theme)));
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

    [Fact]
    public async Task DesktopChromeTransparency_ClampsZeroToOneHundredAndRejectsNonFiniteInput()
    {
        await using var workspace = await ThemeWorkspace.CreateAsync();
        AppThemeManager.ResetBoxOpacitiesForTests();

        try
        {
            await workspace.ViewModel.LoadAsync();

            workspace.ViewModel.BoxBorderTransparencyPercent = -10;
            workspace.ViewModel.IconFrameTransparencyPercent = 200;

            Assert.Equal(0, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(100, workspace.ViewModel.IconFrameTransparencyPercent);

            workspace.ViewModel.BoxBorderTransparencyPercent = double.NaN;
            workspace.ViewModel.IconFrameTransparencyPercent = double.PositiveInfinity;

            Assert.Equal(0, workspace.ViewModel.BoxBorderTransparencyPercent);
            Assert.Equal(100, workspace.ViewModel.IconFrameTransparencyPercent);
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
