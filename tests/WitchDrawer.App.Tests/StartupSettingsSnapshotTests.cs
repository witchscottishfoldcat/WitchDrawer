using System.IO;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class StartupSettingsSnapshotTests
{
    [Fact]
    public async Task BoxViewModel_InitializeFromSnapshot_AppliesPersistedSettings()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            await service.SetSettingAsync(
                BoxViewModel.GetTitleVisibilitySettingKey(box.Id), bool.FalseString);
            await service.SetSettingAsync(
                BoxViewModel.GetFileNameVisibilitySettingKey(box.Id), bool.TrueString);
            await service.SetSettingAsync(
                BoxViewModel.GetHoverRollUpEnabledSettingKey(box.Id), bool.TrueString);
            await service.SetSettingAsync(
                BoxViewModel.GetBoxSortModeSettingKey(box.Id), nameof(DrawerItemSortMode.Size));
            var snapshot = new StartupSettingsSnapshot(await service.GetAllSettingsAsync());

            var viewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await viewModel.InitializeSettingsAsync(snapshot);

            Assert.False(viewModel.IsTitleVisible);
            Assert.True(viewModel.IsFileNameVisible);
            Assert.True(viewModel.IsHoverRollUpEnabled);
            Assert.Equal(DrawerItemSortMode.Size, viewModel.DrawerItemSortMode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task BoxViewModel_Snapshot_KeepsLegacyDrawerKeyFallbacks()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("抽屉盒", BoxType.Drawer);
            // 只有旧键存在：新键缺失时必须回退读取旧键（兼容迁移逻辑）。
            await service.SetSettingAsync(
                BoxViewModel.GetLegacyDrawerTitleVisibilitySettingKey(box.Id), bool.FalseString);
            await service.SetSettingAsync(
                BoxViewModel.GetDrawerSortModeSettingKey(box.Id), nameof(DrawerItemSortMode.ModifiedDate));
            var snapshot = new StartupSettingsSnapshot(await service.GetAllSettingsAsync());

            var viewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await viewModel.InitializeSettingsAsync(snapshot);

            Assert.False(viewModel.IsTitleVisible);
            Assert.Equal(DrawerItemSortMode.ModifiedDate, viewModel.DrawerItemSortMode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task BoxViewModel_RuntimeInitialization_ReadsLiveDatabase()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            await service.SetSettingAsync(
                BoxViewModel.GetTitleVisibilitySettingKey(box.Id), bool.FalseString);

            var viewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await viewModel.InitializeSettingsAsync();

            Assert.False(viewModel.IsTitleVisible);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task BoxViewModel_Snapshot_IsLimitedToCurrentStartup()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            var key = BoxViewModel.GetTitleVisibilitySettingKey(box.Id);
            await service.SetSettingAsync(key, bool.FalseString);
            var snapshot = new StartupSettingsSnapshot(await service.GetAllSettingsAsync());

            // 快照生成后运行期间的修改正常持久化，但快照保持读取时刻的值。
            await service.SetSettingAsync(key, bool.TrueString);

            var staleViewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await staleViewModel.InitializeSettingsAsync(snapshot);
            Assert.False(staleViewModel.IsTitleVisible);

            var liveViewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await liveViewModel.InitializeSettingsAsync();
            Assert.True(liveViewModel.IsTitleVisible);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task BoxViewModel_InitializationFailure_KeepsDefaultsAndDoesNotThrow()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            var box = await service.CreateBoxAsync("普通盒", BoxType.Normal);
            DeleteDirectoryWithRetry(root);

            var viewModel = new BoxViewModel(box, service, BoxVisualStyle.Modern, isPositionLocked: false);
            await viewModel.InitializeSettingsAsync();

            Assert.True(viewModel.IsTitleVisible);
            Assert.False(viewModel.IsFileNameVisible);
            Assert.Equal(DrawerItemSortMode.Free, viewModel.DrawerItemSortMode);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StartupState_LoadsSettingsSnapshotAlongsideHotKey()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = await CreateServiceAsync(root);
            await service.SetSettingAsync("Theme", nameof(AppTheme.Glass));
            var store = new QuickPanelHotKeySettingsStore(service);

            var (settings, hotKey) = await global::WitchDrawer.App.App.InitializeDataAndLoadStartupStateAsync(
                service,
                store);

            Assert.Equal(nameof(AppTheme.Glass), settings.Get("Theme"));
            Assert.Equal(QuickPanelHotKey.Default, hotKey);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task<(DrawerService Service, DrawerRepository Repository)> CreateServiceAsync(string root)
    {
        var paths = new AppPaths(root);
        var repository = new DrawerRepository(paths.DatabasePath);
        var service = new DrawerService(paths, repository);
        await service.InitializeAsync();
        return (service, repository);
    }


    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "WitchDrawerTests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
