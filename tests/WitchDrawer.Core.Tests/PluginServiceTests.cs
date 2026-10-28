using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WitchDrawer.Core.Plugins;

namespace WitchDrawer.Core.Tests;

public sealed class PluginServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WitchDrawerPluginTests", Guid.NewGuid().ToString("N"));
    private static PluginManifest Manifest(string version = "1.0.0") => new()
    {
        Id = "tests.clock", Name = "Test clock", Description = "Clock test", Author = "Tests", Version = version,
        RepositoryUrl = "https://github.com/tests/clock", EntryAssembly = "Clock.dll", EntryType = "Test.Clock"
    };
    private PluginService Service(HttpClient? client = null) => new(new AppPaths(_root), new Version(1, 4, 4, 0), "", http: client);
    private string Package(PluginManifest manifest, params (string Name, byte[] Bytes, int Attributes)[] extra)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "plugin.json", JsonSerializer.SerializeToUtf8Bytes(manifest, PluginJson.Options));
        Add(zip, manifest.EntryAssembly, [1, 2, 3]);
        foreach (var item in extra) Add(zip, item.Name, item.Bytes, item.Attributes);
        return path;
    }
    private static void Add(ZipArchive zip, string name, byte[] bytes, int attributes = 0)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = attributes;
        using var stream = entry.Open(); stream.Write(bytes);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static PluginCatalog Catalog(string hash, PluginManifest? manifest = null) => new()
    {
        Plugins = [new() { Plugin = manifest ?? Manifest(), PackageUrl = "https://github.com/tests/clock/releases/download/v1.0.0/clock.zip", Sha256 = hash }]
    };

    [Fact]
    public async Task InstallIsDisabledAndOfflineStateRestoresSettingsPositionAndEnableIntent()
    {
        var package = Package(Manifest());
        using (var service = Service())
        {
            var installed = await service.InstallLocalAsync(package, Hash(package));
            Assert.False(installed.Enabled);
            Assert.Equal("Clock.dll", Path.GetFileName(await service.VerifyInstalledAsync(installed)));
            await service.SaveSettingsAsync(installed.Manifest.Id, new Dictionary<string, string> { ["seconds"] = "false" });
            await service.SaveLayoutAsync(installed.Manifest.Id, -120, 245, 480, 230);
            await service.SetEnabledAsync(installed.Manifest.Id, true);
        }
        using var restored = Service();
        var item = Assert.Single(await restored.GetInstalledAsync());
        Assert.True(item.Enabled);
        Assert.Equal(-120, item.Left);
        Assert.Equal(245, item.Top);
        Assert.Equal(480, item.Width);
        Assert.Equal(230, item.Height);
        Assert.Equal("false", item.Settings["seconds"]);
    }

    [Fact]
    public async Task UpdatePreservesSettingsAndPositionAndFailureKeepsOldVersion()
    {
        using var service = Service();
        var first = Package(Manifest());
        await service.InstallLocalAsync(first, Hash(first));
        await service.SaveSettingsAsync("tests.clock", new Dictionary<string, string> { ["key"] = "value" });
        await service.SaveLayoutAsync("tests.clock", 123, 456, 450, 210);
        var invalid = Package(Manifest("1.1.0"), ("../outside.txt", [8], 0));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(invalid, Hash(invalid)));
        var unchanged = Assert.Single(await service.GetInstalledAsync());
        Assert.Equal("1.0.0", unchanged.Manifest.Version);
        Assert.Equal(450, unchanged.Width);
        Assert.Equal(210, unchanged.Height);
        Assert.True(File.Exists(await service.VerifyInstalledAsync(unchanged)));
        var next = Package(Manifest("1.1.0"));
        var updated = await service.InstallLocalAsync(next, Hash(next));
        Assert.False(updated.Enabled);
        Assert.Equal("value", updated.Settings["key"]);
        Assert.Equal(123, updated.Left);
        Assert.Equal(456, updated.Top);
        Assert.Equal(450, updated.Width);
        Assert.Equal(210, updated.Height);
        Assert.Equal("1.1.0", Assert.Single(await service.GetInstalledAsync()).Manifest.Version);
    }

    [Theory]
    [InlineData(double.NaN, 120)]
    [InlineData(300, double.PositiveInfinity)]
    [InlineData(79, 120)]
    [InlineData(300, 63)]
    [InlineData(8193, 120)]
    [InlineData(300, 8193)]
    public async Task InvalidSizeDoesNotOverwriteInstalledLayout(double width, double height)
    {
        using var service = Service();
        var package = Package(Manifest());
        await service.InstallLocalAsync(package, Hash(package));
        await service.SaveLayoutAsync("tests.clock", 15, 20, 320, 160);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveLayoutAsync("tests.clock", 999, 999, width, height));
        var installed = Assert.Single(await service.GetInstalledAsync());
        Assert.Equal(15, installed.Left);
        Assert.Equal(20, installed.Top);
        Assert.Equal(320, installed.Width);
        Assert.Equal(160, installed.Height);
    }

    [Fact]
    public async Task OldInstalledStateWithoutSizeStillLoads()
    {
        using var service = Service();
        var package = Package(Manifest());
        await service.InstallLocalAsync(package, Hash(package));
        var path = Path.Combine(_root, "Plugins", "installed.json");
        var state = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var plugin = state["plugins"]![0]!.AsObject();
        plugin.Remove("width"); plugin.Remove("height");
        await File.WriteAllTextAsync(path, state.ToJsonString());
        var installed = Assert.Single(await service.GetInstalledAsync());
        Assert.Null(installed.Width);
        Assert.Null(installed.Height);
    }

    [Fact]
    public async Task RunningPluginCannotBeUpdatedOrUninstalled()
    {
        using var service = Service();
        var first = Package(Manifest());
        await service.InstallLocalAsync(first, Hash(first));
        await service.SetEnabledAsync("tests.clock", true);
        var next = Package(Manifest("1.1.0"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallLocalAsync(next, Hash(next)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync("tests.clock"));
        await service.SetEnabledAsync("tests.clock", false);
        await service.UninstallAsync("tests.clock");
        Assert.Empty(await service.GetInstalledAsync());
        Assert.True(File.Exists(first)); // Import never deletes the user's source package.
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("folder\\escape.txt")]
    [InlineData("NUL.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("folder./file.txt")]
    [InlineData("CLOCK.dll")]
    [InlineData("WitchDrawer.PluginSdk.dll")]
    public async Task UnsafeOrAliasedPackagePathsDoNotInstall(string name)
    {
        using var service = Service();
        var package = Package(Manifest(), (name, [8], 0));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(package, Hash(package)));
        Assert.Empty(await service.GetInstalledAsync());
        Assert.False(File.Exists(Path.Combine(_root, "Plugins", "outside.txt")));
    }

    [Fact]
    public async Task ZipLinksAreRejected()
    {
        using var service = Service();
        var package = Package(Manifest(), ("link", Encoding.UTF8.GetBytes("../elsewhere"), unchecked((int)0xA1FF0000)));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(package, Hash(package)));
        Assert.Empty(await service.GetInstalledAsync());
    }

    [Fact]
    public async Task ChangedLocalPackageAndChangedInstalledDllAreRejected()
    {
        using var service = Service();
        var package = Package(Manifest());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(package, new string('0', 64)));
        var installed = await service.InstallLocalAsync(package, Hash(package));
        var dll = await service.VerifyInstalledAsync(installed);
        File.WriteAllText(dll, "modified");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.VerifyInstalledAsync(installed));
    }

    [Fact]
    public async Task IncompatibleOrDowngradedPackagesAreRejected()
    {
        using var service = Service();
        var incompatible = Package(Manifest() with { MinHostVersion = "9.0.0" });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(incompatible, Hash(incompatible)));
        var current = Package(Manifest("2.0.0"));
        await service.InstallLocalAsync(current, Hash(current));
        var older = Package(Manifest());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLocalAsync(older, Hash(older)));
        Assert.Equal("2.0.0", Assert.Single(await service.GetInstalledAsync()).Manifest.Version);
    }

    [Fact]
    public void ExactMaximumVersionIncludesAssemblyRevisionZero()
    {
        Assert.True(PluginValidation.IsCompatible(Manifest() with { MaxHostVersion = "1.4.4" }, new Version(1, 4, 4, 0)));
        Assert.False(PluginValidation.IsCompatible(Manifest() with { MaxHostVersion = "1.4.4" }, new Version(1, 4, 5, 0)));
    }

    [Fact]
    public async Task CorruptedStateIsPreservedInsteadOfOverwritten()
    {
        using var service = Service();
        var package = Package(Manifest());
        var directory = Path.Combine(_root, "Plugins"); Directory.CreateDirectory(directory);
        var state = Path.Combine(directory, "installed.json"); File.WriteAllText(state, "broken");
        await Assert.ThrowsAsync<JsonException>(() => service.InstallLocalAsync(package, Hash(package)));
        Assert.Equal("broken", File.ReadAllText(state));
    }

    [Fact]
    public async Task CancelledInstallDoesNotPublishState()
    {
        using var service = Service();
        var package = Package(Manifest());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallLocalAsync(package, Hash(package), cancelled.Token));
        Assert.Empty(await service.GetInstalledAsync());
    }

    [Fact]
    public async Task CatalogRefreshFallsBackToLastGoodCatalogAfterInvalidResponse()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Catalog(new string('1', 64)), PluginJson.Options);
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(++calls == 1 ? bytes : Encoding.UTF8.GetBytes("broken")) }));
        using var service = Service(http);
        Assert.False((await service.RefreshCatalogAsync()).Offline);
        var fallback = await service.RefreshCatalogAsync();
        Assert.True(fallback.Offline);
        Assert.Equal("tests.clock", Assert.Single(fallback.Catalog.Plugins).Plugin.Id);
    }

    [Fact]
    public async Task CatalogInstallChecksReviewedManifestAndHash()
    {
        var package = Package(Manifest()); var bytes = File.ReadAllBytes(package);
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        using var service = Service(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallFromCatalogAsync(Catalog(new string('0', 64)).Plugins[0]));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallFromCatalogAsync(Catalog(Hash(package), Manifest() with { Author = "Other" }).Plugins[0]));
        Assert.Empty(await service.GetInstalledAsync());
        Assert.False((await service.InstallFromCatalogAsync(Catalog(Hash(package)).Plugins[0])).Enabled);
    }

    [Fact]
    public async Task ReleaseRedirectCanDownloadButRedirectToUnexpectedHostCannot()
    {
        var package = Package(Manifest()); var bytes = File.ReadAllBytes(package);
        var evil = false;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host != "github.com") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri(evil ? "https://evil.example/package.zip" : "https://release-assets.githubusercontent.com/asset.zip");
            return redirect;
        }));
        using var service = Service(http);
        evil = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallFromCatalogAsync(Catalog(Hash(package)).Plugins[0]));
        evil = false;
        await service.InstallFromCatalogAsync(Catalog(Hash(package)).Plugins[0]);
        Assert.Single(await service.GetInstalledAsync());
    }

    [Theory]
    [InlineData("https://github.com/tests/clock/releases/latest/download/clock.zip")]
    [InlineData("https://github.com/other/clock/releases/download/v1/clock.zip")]
    [InlineData("https://evil.example/clock.zip")]
    [InlineData("http://github.com/tests/clock/releases/download/v1/clock.zip")]
    public void CatalogRequiresPinnedSameRepositoryHttpsReleaseUrl(string url)
    {
        var entry = Catalog(new string('1', 64)).Plugins[0] with { PackageUrl = url };
        Assert.Throws<InvalidDataException>(() => PluginValidation.Validate(new PluginCatalog { Plugins = [entry] }));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    public void Dispose()
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WitchDrawerPluginTests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(allowed, Path.GetFullPath(_root));
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
