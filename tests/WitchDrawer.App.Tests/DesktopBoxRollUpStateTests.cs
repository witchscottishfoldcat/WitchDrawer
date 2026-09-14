using System.IO;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxRollUpStateTests
{
    [Fact]
    public void RolledUpWindowHeight_IncludesHeaderBorderAndOuterMargin()
    {
        var height = DesktopBoxWindow.CalculateRolledUpWindowHeight(
            new System.Windows.Thickness(4, 5, 6, 7),
            new System.Windows.Thickness(1, 2, 3, 4),
            DesktopBoxViewModel.VisibleHeaderRowHeight);

        Assert.Equal(42, height);
    }

    [Fact]
    public async Task RollUpState_RoundTripsThroughSettingsDatabase()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("卷起状态测试盒", BoxType.Normal);
            var viewModel = CreateViewModel(box, drawerService, repository);

            viewModel.ApplyRollUpState(true);
            await viewModel.SaveRollUpStateAsync();

            Assert.Equal(
                bool.TrueString,
                await drawerService.GetSettingAsync(
                    DesktopBoxViewModel.GetRollUpSettingKey(box.Id)));

            var reloadedRepository = new DrawerRepository(paths.DatabasePath);
            var reloadedDrawerService = new DrawerService(paths, reloadedRepository);
            await reloadedDrawerService.InitializeAsync();
            var reloadedViewModel = CreateViewModel(
                box,
                reloadedDrawerService,
                reloadedRepository);

            Assert.False(reloadedViewModel.IsRolledUp);

            await reloadedViewModel.LoadRollUpStateAsync();

            Assert.True(reloadedViewModel.IsRolledUp);
            Assert.True(reloadedViewModel.IsHeaderVisible);
            Assert.True(reloadedViewModel.IsHeaderTitleVisible);
            Assert.Equal(0, reloadedViewModel.ContentRowHeight.Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HoverRollUpSetting_DefaultsOffAndRoundTripsThroughBoxSettings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("悬停展开设置测试盒", BoxType.Normal);
            var desktopViewModel = CreateViewModel(box, drawerService, repository);
            var settingsViewModel = new BoxViewModel(
                box,
                drawerService,
                BoxVisualStyle.Modern,
                isPositionLocked: false);

            await desktopViewModel.LoadHoverRollUpEnabledAsync();
            await settingsViewModel.LoadHoverRollUpEnabledAsync();

            Assert.False(desktopViewModel.IsHoverRollUpEnabled);
            Assert.False(settingsViewModel.IsHoverRollUpEnabled);
            Assert.Equal(
                BoxViewModel.GetHoverRollUpEnabledSettingKey(box.Id),
                DesktopBoxViewModel.GetHoverRollUpEnabledSettingKey(box.Id));

            await settingsViewModel.ToggleHoverRollUpCommand.ExecuteAsync(null);
            await desktopViewModel.LoadHoverRollUpEnabledAsync();

            Assert.True(settingsViewModel.IsHoverRollUpEnabled);
            Assert.True(desktopViewModel.IsHoverRollUpEnabled);
            Assert.Equal(
                bool.TrueString,
                await drawerService.GetSettingAsync(
                    DesktopBoxViewModel.GetHoverRollUpEnabledSettingKey(box.Id)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DesktopBoxViewModel CreateViewModel(
        Box box,
        DrawerService drawerService,
        DrawerRepository repository) =>
        new(
            box,
            drawerService,
            new TodoService(repository),
            new NoOpFileLauncher(),
            new RecordingLogger(),
            BoxVisualStyle.Modern);

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default) =>
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
