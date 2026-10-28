using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Plugins;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class PluginStorageRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WitchDrawerPluginStorageTests", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private static PluginManifest Manifest => new()
    {
        Id = "tests.storage", Name = "Storage", Author = "Tests", Description = "Storage test", Version = "1.0.0",
        EntryAssembly = "widget.dll", EntryType = "Tests.Widget", RepositoryUrl = "https://github.com/tests/storage"
    };

    private PluginService Service(string? root = null, HttpClient? http = null) =>
        new(new AppPaths(root ?? Source), new Version(1, 4, 4), "", http: http);

    private async Task<InstalledPlugin> InstallAsync(PluginService service, string entryName = "widget.dll")
    {
        Directory.CreateDirectory(_root);
        var package = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open()))
                writer.Write(JsonSerializer.Serialize(Manifest, PluginJson.Options));
            using var assembly = zip.CreateEntry(entryName).Open();
            assembly.Write([1, 2, 3]); // Package validation never executes this fixture.
        }
        return await service.InstallLocalAsync(package, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))));
    }

    [Fact]
    public async Task ConcurrentReadersAndWritersAcrossServicesPreserveStateWithoutSharingFailures()
    {
        using var first = Service();
        using var second = Service();
        await InstallAsync(first);
        var settings = Enumerable.Range(0, 100).ToDictionary(i => "key" + i, _ => new string('x', 4000));
        await first.SaveSettingsAsync(Manifest.Id, settings);

        async Task ReadAsync(PluginService service)
        {
            for (var i = 0; i < 100; i++) Assert.Single(await service.GetInstalledAsync());
        }
        async Task SaveLayoutsAsync()
        {
            for (var i = 0; i < 60; i++) await first.SaveLayoutAsync(Manifest.Id, i, i, 160, 100);
        }
        async Task SaveSettingsAsync()
        {
            for (var i = 0; i < 30; i++) await second.SaveSettingsAsync(Manifest.Id, settings);
        }
        await Task.WhenAll(ReadAsync(first), ReadAsync(second), SaveLayoutsAsync(), SaveSettingsAsync());

        var saved = Assert.Single(await second.GetInstalledAsync());
        Assert.Equal(59, saved.Left);
        Assert.Equal(160, saved.Width);
        Assert.Equal(100, saved.Settings.Count);
        Assert.Equal(settings["key99"], saved.Settings["key99"]);
    }

    [Fact]
    public async Task ReloadedPackageIndexPreservesWindowsPathCaseSemantics()
    {
        using var service = Service();
        await InstallAsync(service, "WIDGET.dll");
        using var restarted = Service();
        var installed = Assert.Single(await restarted.GetInstalledAsync());

        Assert.True(installed.Files.ContainsKey("widget.dll"));
        Assert.True(File.Exists(await restarted.VerifyInstalledAsync(installed)));
    }

    [Fact]
    public async Task StalledCatalogBodyTimesOutAndReturnsOfflineCatalog()
    {
        using var cancelled = new CancellationTokenSource();
        using var http = new HttpClient(new StallHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var service = Service(http: http);
        var refresh = service.RefreshCatalogAsync(cancelled.Token);
        try
        {
            Assert.Same(refresh, await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(3))));
            Assert.True((await refresh).Offline);
            Assert.Empty(await service.GetInstalledAsync());
        }
        finally { cancelled.Cancel(); }
    }

    [Fact]
    public async Task StalledPackageBodyHonorsDeadlineWithoutPublishingAnInstall()
    {
        using var cancelled = new CancellationTokenSource();
        using var http = new HttpClient(new StallHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var service = Service(http: http);
        var entry = new PluginCatalogEntry { Plugin = Manifest, Sha256 = new string('1', 64), PackageUrl = "https://github.com/tests/storage/releases/download/v1.0.0/widget.zip" };
        var install = service.InstallFromCatalogAsync(entry, cancelled.Token);
        try
        {
            Assert.Same(install, await Task.WhenAny(install, Task.Delay(TimeSpan.FromSeconds(3))));
            await Assert.ThrowsAsync<TimeoutException>(() => install);
            Assert.Empty(await service.GetInstalledAsync());
        }
        finally { cancelled.Cancel(); }
    }

    [Fact]
    public async Task StalledBodyPropagatesExplicitCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        using var http = new HttpClient(new StallHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        using var service = Service(http: http);
        var refresh = service.RefreshCatalogAsync(cancelled.Token);
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task MigrationRetargetsLiveAndNewServicesAndKeepsTheOldSnapshotUnchanged()
    {
        using var live = Service();
        await InstallAsync(live);
        await live.SaveLayoutAsync(Manifest.Id, 10, 20, 160, 100);
        var target = Path.Combine(_root, "target");
        using var destinationService = Service(target); // Already-existing target clients also join the cutover.
        var migration = await MigrationAsync();

        await migration.MigrateAsync(target);
        await live.SaveLayoutAsync(Manifest.Id, 999, 888, 320, 200);
        await destinationService.SaveSettingsAsync(Manifest.Id, new Dictionary<string, string> { ["saved"] = "after migration" });
        using var restarted = Service(target);
        using var lateOldClient = Service();
        foreach (var client in new[] { live, destinationService, restarted, lateOldClient })
        {
            var installed = Assert.Single(await client.GetInstalledAsync());
            Assert.Equal(999, installed.Left);
            Assert.Equal("after migration", installed.Settings["saved"]);
            Assert.StartsWith(target, await client.VerifyInstalledAsync(installed), StringComparison.OrdinalIgnoreCase);
        }
        using var snapshot = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Source, "Plugins", "installed.json")));
        Assert.Equal(10, snapshot.RootElement.GetProperty("plugins")[0].GetProperty("left").GetDouble());
    }

    [Fact]
    public async Task FailedMigrationReleasesPluginWritesWithoutChangingTheirDirectory()
    {
        using var service = Service();
        await InstallAsync(service);
        var migration = await MigrationAsync();
        var target = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");

        await Assert.ThrowsAsync<InvalidOperationException>(() => migration.MigrateAsync(target));
        await service.SaveLayoutAsync(Manifest.Id, 55, 66, 160, 100).WaitAsync(TimeSpan.FromSeconds(3));
        var installed = Assert.Single(await service.GetInstalledAsync());
        Assert.Equal(55, installed.Left);
        Assert.StartsWith(Source, await service.VerifyInstalledAsync(installed));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public async Task QueuedWritesWaitForTheMigrationLeaseAndResolveItsDestinationAfterPromotion()
    {
        using var service = Service();
        await InstallAsync(service);
        var target = Path.Combine(_root, "target");
        var migration = await PluginStorageCoordinator.BeginMigrationAsync(Source, target, CancellationToken.None);
        Task pending;
        try
        {
            CopyDirectory(Path.Combine(Source, "Plugins"), Path.Combine(target, "Plugins"));
            pending = service.SaveLayoutAsync(Manifest.Id, 123, 456, 160, 100);
            Assert.False(pending.IsCompleted);
            migration.Promote();
            Assert.False(pending.IsCompleted);
        }
        finally { migration.Dispose(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        using var restarted = Service(target);
        Assert.Equal(123, Assert.Single(await restarted.GetInstalledAsync()).Left);
    }

    private async Task<DataStorageMigrationService> MigrationAsync()
    {
        var paths = new AppPaths(Source);
        paths.EnsureCreated();
        var repository = new DrawerRepository(paths.DatabasePath);
        await repository.InitializeAsync();
        return new(paths, repository, new StorageLocationStore(Path.Combine(_root, "bootstrap", StorageLocationStore.ConfigFileName)));
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private sealed class StallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()) });
    }

    private sealed class StallStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "WitchDrawerPluginStorageTests") + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
