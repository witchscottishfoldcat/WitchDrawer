using System.IO;
using WitchDrawer.App.ViewModels;
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
    public async Task RollUpState_RoundTripsThroughSettingsDatabase()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawerTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            
            using var drawerService = new RustDrawerService(paths.RootDirectory);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("卷起状态测试盒", BoxType.Normal);
            var viewModel = CreateViewModel(box, drawerService);

            viewModel.ApplyRollUpState(true);
            await viewModel.SaveRollUpStateAsync();

            Assert.Equal(
                bool.TrueString,
                await drawerService.GetSettingAsync(
                    DesktopBoxViewModel.GetRollUpSettingKey(box.Id)));

            using var reloadedDrawerService = new RustDrawerService(paths.RootDirectory);
            await reloadedDrawerService.InitializeAsync();
            var reloadedViewModel = CreateViewModel(
                box,
                reloadedDrawerService);

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
                TestCleanup.DeleteDirectory(root);
            }
        }
    }

    private static DesktopBoxViewModel CreateViewModel(
        Box box,
        RustDrawerService drawerService) =>
        new(
            box,
            drawerService,
            new RustTodoService(drawerService),
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
