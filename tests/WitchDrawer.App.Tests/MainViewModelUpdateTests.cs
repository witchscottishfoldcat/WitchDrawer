using WitchDrawer.App.Services;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.App.Tests;

// Verifies the update-flow state machine on MainViewModel: distinct check vs. in-progress
// flags, non-reentrancy across check -> prompt -> download, and that UpdateRequested fires
// exactly once when an update is available. Uses a fake IUpdateService so no network is hit.
public sealed class MainViewModelUpdateTests
{
    [Fact]
    public async Task CheckForUpdate_FiresUpdateRequestedAndSetsCheckingFlag()
    {
        var fixture = await MainViewModelFixture.CreateAsync(hasUpdate: true);

        UpdateCheckResult? captured = null;
        fixture.ViewModel.UpdateRequested += (_, result) => captured = result;

        await fixture.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.True(captured!.HasUpdate);
        // After the check settles (prompt not yet answered), IsCheckingUpdate must be false but
        // the reentrancy guard must remain held so a second check cannot stack a second prompt.
        Assert.False(fixture.ViewModel.IsCheckingUpdate);
        Assert.False(fixture.ViewModel.CheckForUpdateCommand.CanExecute(null));

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task CheckForUpdate_NoUpdateKeepsCommandReexecutable()
    {
        var fixture = await MainViewModelFixture.CreateAsync(hasUpdate: false);

        await fixture.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.IsCheckingUpdate);
        // No prompt is shown, so the guard is released and the user can check again.
        Assert.True(fixture.ViewModel.CheckForUpdateCommand.CanExecute(null));

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteUpdate_SetsInProgressFlagThenReleasesGuard()
    {
        var fixture = await MainViewModelFixture.CreateAsync(hasUpdate: true);

        UpdateCheckResult? captured = null;
        fixture.ViewModel.UpdateRequested += (_, result) => captured = result;
        await fixture.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        await fixture.ViewModel.ExecuteUpdateAsync(captured!.DownloadUrl);

        Assert.False(fixture.ViewModel.IsUpdateInProgress);
        Assert.True(fixture.ViewModel.CheckForUpdateCommand.CanExecute(null));

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ReleaseUpdateGuard_AllowsFurtherChecksAfterCancel()
    {
        var fixture = await MainViewModelFixture.CreateAsync(hasUpdate: true);

        UpdateCheckResult? captured = null;
        fixture.ViewModel.UpdateRequested += (_, result) => captured = result;
        await fixture.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.CheckForUpdateCommand.CanExecute(null));

        // Simulate the user cancelling the prompt: App.xaml.cs calls ReleaseUpdateGuard.
        fixture.ViewModel.ReleaseUpdateGuard();
        Assert.True(fixture.ViewModel.CheckForUpdateCommand.CanExecute(null));

        await fixture.DisposeAsync();
    }

    private sealed class MainViewModelFixture : IAsyncDisposable
    {
        private readonly DrawerRepository _repository;
        private readonly TestWorkspaceCleanup _cleanup;

        private MainViewModelFixture(MainViewModel viewModel, DrawerRepository repository, TestWorkspaceCleanup cleanup)
        {
            ViewModel = viewModel;
            _repository = repository;
            _cleanup = cleanup;
        }

        public MainViewModel ViewModel { get; }

        public static async Task<MainViewModelFixture> CreateAsync(bool hasUpdate)
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.App.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            paths.EnsureCreated();

            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();

            var logger = new NullLogger();
            var launcher = new NoopLauncher();
            var trash = new NoopTrash();
            var layout = new DesktopBoxLayoutSettings();
            var updateService = new FakeUpdateService(hasUpdate);
            var quickPanel = new QuickPanelViewModel(drawerService, launcher, logger);

            var viewModel = new MainViewModel(
                drawerService, launcher, trash, logger, quickPanel, layout, updateService);

            return new MainViewModelFixture(viewModel, repository, new TestWorkspaceCleanup(root));
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Run(() => _cleanup.Dispose());
        }
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly bool _hasUpdate;

        public FakeUpdateService(bool hasUpdate)
        {
            _hasUpdate = hasUpdate;
        }

        public event Action<int>? DownloadProgressChanged;

        public Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion)
        {
            return Task.FromResult(_hasUpdate
                ? new UpdateCheckResult { HasUpdate = true, LatestVersion = new Version(9, 9, 9), DownloadUrl = "https://example.com/update.zip" }
                : new UpdateCheckResult());
        }

        public Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<int>? progress = null, string? expectedSha256 = null)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class TestWorkspaceCleanup : IDisposable
    {
        private readonly string _root;
        public TestWorkspaceCleanup(string root) { _root = root; }
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }
    }

    private sealed class NoopLauncher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopTrash : IFileTrash
    {
        public Task MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
