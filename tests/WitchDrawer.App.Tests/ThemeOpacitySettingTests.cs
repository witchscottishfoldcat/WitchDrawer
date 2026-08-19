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
