using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.Core.Plugins;

public sealed record PluginPackagePreview(PluginManifest Manifest, string Sha256);
public sealed record PluginCatalogResult(PluginCatalog Catalog, bool Offline);

/// <summary>Owns all package/cache/state I/O. No network traffic until explicitly requested.</summary>
public sealed class PluginService : IDisposable
{
    public const string CatalogUrl = "https://raw.githubusercontent.com/witchscottishfoldcat/WitchDrawer.Plugins/main/plugins/catalog.json";
    public const string SubmissionUrl = "https://github.com/witchscottishfoldcat/WitchDrawer.Plugins/issues/new?template=plugin-submission.yml";
    private readonly PluginStorageCoordinator _storage;
    private string _root => _storage.Directory;
    private readonly string _bundledCatalog;
    private readonly Version _hostVersion;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _downloadTimeout;
    private readonly TimeSpan _downloadIdleTimeout;
    private string StatePath => Path.Combine(_root, "installed.json");
    private string CachePath => Path.Combine(_root, "catalog.json");

    public PluginService(AppPaths paths, Version hostVersion, string bundledCatalog,
        IAppLogger? logger = null, HttpClient? http = null)
    {
        _storage = PluginStorageCoordinator.ForDataDirectory(paths.RootDirectory);
        _bundledCatalog = bundledCatalog;
        _hostVersion = hostVersion;
        _logger = logger ?? NullAppLogger.Instance;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _downloadTimeout = _http.Timeout == Timeout.InfiniteTimeSpan ? TimeSpan.FromMinutes(5) : _http.Timeout;
        _downloadIdleTimeout = TimeSpan.FromSeconds(30);
        if (_ownsHttp) _http.DefaultRequestHeaders.UserAgent.ParseAdd("WitchDrawer-Plugins/1.0");
    }

    public bool IsCompatible(PluginManifest manifest) => PluginValidation.IsCompatible(manifest, _hostVersion);

    public async Task<IReadOnlyList<InstalledPlugin>> GetInstalledAsync(CancellationToken cancellationToken = default)
    {
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run<IReadOnlyList<InstalledPlugin>>(() => ReadState().Plugins, cancellationToken).ConfigureAwait(false);
    }

    public Task<PluginPackagePreview> PreviewPackageAsync(string packagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var file = OpenPackage(packagePath);
            var hash = Convert.ToHexString(SHA256.HashData(file));
            file.Position = 0;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            return new PluginPackagePreview(ReadManifest(zip), hash);
        }, cancellationToken);

    public async Task<PluginCatalogResult> RefreshCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await DownloadAsync(new Uri(CatalogUrl), PluginValidation.MaxCatalogBytes, false, cancellationToken).ConfigureAwait(false);
            using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
            var catalog = await Task.Run(() =>
            {
                var parsed = Deserialize<PluginCatalog>(bytes);
                PluginValidation.Validate(parsed);
                WriteAtomic(CachePath, bytes);
                return parsed;
            }, cancellationToken).ConfigureAwait(false);
            return new(catalog, false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or TaskCanceledException or TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Error(exception, "Plugin catalog unavailable; using offline catalog.");
            return new(await GetCachedCatalogAsync(cancellationToken).ConfigureAwait(false), true);
        }
    }

    public async Task<PluginCatalog> GetCachedCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            foreach (var path in new[] { CachePath, _bundledCatalog })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    if (new FileInfo(path).Length > PluginValidation.MaxCatalogBytes) throw new InvalidDataException("Oversized catalog.");
                    var catalog = Deserialize<PluginCatalog>(File.ReadAllBytes(path));
                    PluginValidation.Validate(catalog);
                    return catalog;
                }
                catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
                { _logger.Error(exception, "Invalid cached plugin catalog."); }
            }
            return new PluginCatalog();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstalledPlugin> InstallFromCatalogAsync(PluginCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        PluginValidation.Validate(new PluginCatalog { Plugins = [entry] });
        if (!IsCompatible(entry.Plugin)) throw new InvalidDataException("Incompatible plugin.");
        var bytes = await DownloadAsync(PluginValidation.RequireHttps(entry.PackageUrl), PluginValidation.MaxPackageBytes, true, cancellationToken).ConfigureAwait(false);
        return await InstallBytesAsync(bytes, entry.Sha256, entry.Plugin, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The caller obtains explicit trust confirmation for local packages and supplies the preview hash.</summary>
    public async Task<InstalledPlugin> InstallLocalAsync(string packagePath, string previewHash, CancellationToken cancellationToken = default)
    {
        PluginValidation.ValidateHash(previewHash);
        var bytes = await Task.Run(() =>
        {
            using var file = OpenPackage(packagePath);
            using var output = new MemoryStream();
            file.CopyTo(output);
            return output.ToArray();
        }, cancellationToken).ConfigureAwait(false);
        return await InstallBytesAsync(bytes, previewHash, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InstalledPlugin> InstallBytesAsync(byte[] bytes, string hash, PluginManifest? expected, CancellationToken cancellationToken)
    {
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > PluginValidation.MaxPackageBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package SHA-256 mismatch.");
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var manifest = ReadManifest(zip);
            if (!IsCompatible(manifest)) throw new InvalidDataException("Incompatible plugin.");
            if (expected is not null && JsonSerializer.Serialize(manifest, PluginJson.Options) != JsonSerializer.Serialize(expected, PluginJson.Options))
                throw new InvalidDataException("Package manifest does not match the reviewed catalog.");
            var state = ReadState();
            var old = state.Plugins.Find(p => p.Manifest.Id == manifest.Id);
            if (old?.Enabled == true) throw new InvalidOperationException("Disable the plugin before updating it.");
            if (old is not null && PluginValidation.ParseVersion(manifest.Version) <= PluginValidation.ParseVersion(old.Manifest.Version))
                throw new InvalidDataException("Plugin updates must increase the version.");
            var generation = Guid.NewGuid().ToString("N");
            var directory = PackageDirectory(manifest.Id, generation);
            EnsureSafeDirectory(directory);
            try
            {
                var files = Extract(zip, directory, cancellationToken);
                if (!files.ContainsKey(manifest.EntryAssembly)) throw new InvalidDataException("Entry assembly is missing.");
                var installed = new InstalledPlugin
                {
                    Manifest = manifest, Generation = generation, Sha256 = hash, Files = files,
                    Settings = old?.Settings ?? [], Left = old?.Left, Top = old?.Top,
                    Width = old?.Width, Height = old?.Height
                };
                var plugins = state.Plugins.Where(p => p.Manifest.Id != manifest.Id).Append(installed).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                WriteState(state with { Plugins = plugins });
                if (old is not null) TryDeletePackage(old);
                return installed;
            }
            catch
            {
                TryDeleteDirectory(directory);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default) =>
        MutateAsync(id, plugin => plugin with { Enabled = enabled }, cancellationToken);

    public Task SavePositionAsync(string id, double left, double top, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)) throw new ArgumentException("Invalid widget position.");
        return MutateAsync(id, p => p with { Left = left, Top = top }, cancellationToken);
    }

    public Task SaveLayoutAsync(string id, double left, double top, double width, double height, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !IsValidSize(width, height))
            throw new ArgumentException("Invalid widget layout.");
        return MutateAsync(id, p => p with { Left = left, Top = top, Width = width, Height = height }, cancellationToken);
    }

    private static bool IsValidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width is >= 80 and <= 8192 && height is >= 64 and <= 8192;

    public Task SaveSettingsAsync(string id, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        if (settings.Count > 100 || settings.Any(kv => kv.Key.Length > 100 || kv.Value is null || kv.Value.Length > 4000))
            throw new ArgumentException("Oversized plugin settings.");
        var copy = settings.ToDictionary(kv => kv.Key, kv => kv.Value);
        return MutateAsync(id, p => p with { Settings = copy }, cancellationToken);
    }

    private async Task MutateAsync(string id, Func<InstalledPlugin, InstalledPlugin> mutate, CancellationToken cancellationToken)
    {
        PluginValidation.ValidateId(id);
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        await Task.Run(() =>
        {
            var state = ReadState();
            var index = state.Plugins.FindIndex(p => p.Manifest.Id == id);
            if (index < 0) throw new InvalidOperationException("Plugin is not installed.");
            state.Plugins[index] = mutate(state.Plugins[index]);
            WriteState(state);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UninstallAsync(string id, CancellationToken cancellationToken = default)
    {
        PluginValidation.ValidateId(id);
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        await Task.Run(() =>
        {
            var state = ReadState();
            var old = state.Plugins.Find(p => p.Manifest.Id == id);
            if (old is null) return;
            if (old.Enabled) throw new InvalidOperationException("Disable the plugin before uninstalling it.");
            WriteState(state with { Plugins = state.Plugins.Where(p => p.Manifest.Id != id).ToList() });
            TryDeletePackage(old);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> VerifyInstalledAsync(InstalledPlugin plugin, CancellationToken cancellationToken = default)
    {
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            PluginValidation.Validate(plugin.Manifest);
            var directory = PackageDirectory(plugin.Manifest.Id, plugin.Generation);
            if (!IsCompatible(plugin.Manifest)) throw new InvalidDataException("Incompatible plugin.");
            if (plugin.Files.Count == 0 || plugin.Files.Count > PluginValidation.MaxFiles || !plugin.Files.ContainsKey(plugin.Manifest.EntryAssembly))
                throw new InvalidDataException("Invalid installed package index.");
            foreach (var (relative, expected) in plugin.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PluginValidation.ValidateHash(expected);
                var path = PackageFile(directory, relative);
                EnsureNoReparsePoints(path);
                using var file = File.OpenRead(path);
                if (file.Length > PluginValidation.MaxExpandedBytes || !Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installed plugin was modified; reinstall it.");
            }
            var manifest = Deserialize<PluginManifest>(File.ReadAllBytes(Path.Combine(directory, "plugin.json")));
            if (JsonSerializer.Serialize(manifest, PluginJson.Options) != JsonSerializer.Serialize(plugin.Manifest, PluginJson.Options))
                throw new InvalidDataException("Installed manifest was modified.");
            return PackageFile(directory, plugin.Manifest.EntryAssembly);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Old DLLs may stay locked until process exit; clean unused generations on an explicit management visit.</summary>
    public async Task CleanupUnusedAsync(CancellationToken cancellationToken = default)
    {
        using var storage = await _storage.EnterAsync(cancellationToken).ConfigureAwait(false);
        await Task.Run(() =>
        {
            var packages = Path.Combine(_root, "packages");
            if (!Directory.Exists(packages)) return;
            EnsureNoReparsePoints(packages);
            var active = ReadState().Plugins.Select(p => PackageDirectory(p.Manifest.Id, p.Generation)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var idDirectory in Directory.EnumerateDirectories(packages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(idDirectory);
                foreach (var generationDirectory in Directory.EnumerateDirectories(idDirectory))
                    if (!active.Contains(generationDirectory)) TryDeleteDirectory(generationDirectory);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private PluginState ReadState()
    {
        if (!File.Exists(StatePath)) return new();
        EnsureNoReparsePoints(StatePath);
        if (new FileInfo(StatePath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Oversized plugin state.");
        var state = Deserialize<PluginState>(File.ReadAllBytes(StatePath));
        if (state.SchemaVersion != 1 || state.Plugins is null || state.Plugins.Count > 1000) throw new InvalidDataException("Invalid plugin state.");
        var ids = new HashSet<string>();
        for (var index = 0; index < state.Plugins.Count; index++)
        {
            var plugin = state.Plugins[index];
            PluginValidation.Validate(plugin.Manifest);
            PackageDirectory(plugin.Manifest.Id, plugin.Generation);
            PluginValidation.ValidateHash(plugin.Sha256);
            if (!ids.Add(plugin.Manifest.Id) || plugin.Settings is null || plugin.Files is null
                || plugin.Left is { } left && !double.IsFinite(left) || plugin.Top is { } top && !double.IsFinite(top)
                || (plugin.Width is null) != (plugin.Height is null)
                || plugin.Width is { } width && plugin.Height is { } height && !IsValidSize(width, height))
                throw new InvalidDataException("Invalid plugin state.");
            try { state.Plugins[index] = plugin with { Files = new(plugin.Files, StringComparer.OrdinalIgnoreCase) }; }
            catch (ArgumentException exception) { throw new InvalidDataException("Duplicate installed package paths.", exception); }
        }
        return state;
    }

    private void WriteState(PluginState state) => WriteAtomic(StatePath, JsonSerializer.SerializeToUtf8Bytes(state, PluginJson.Options));

    private void WriteAtomic(string path, byte[] bytes)
    {
        EnsureSafeDirectory(Path.GetDirectoryName(path)!);
        EnsureNoReparsePoints(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static FileStream OpenPackage(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > PluginValidation.MaxPackageBytes) { stream.Dispose(); throw new InvalidDataException("Oversized package."); }
        return stream;
    }

    private static T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, PluginJson.Options)
        ?? throw new InvalidDataException("Invalid plugin JSON.");

    private static PluginManifest ReadManifest(ZipArchive zip)
    {
        if (zip.Entries.Count > PluginValidation.MaxFiles) throw new InvalidDataException("Too many package entries.");
        var entries = zip.Entries.Where(e => e.FullName == "plugin.json").ToArray();
        if (entries.Length != 1 || entries[0].Length > 32768) throw new InvalidDataException("Package requires one root plugin.json.");
        using var input = entries[0].Open();
        using var output = new MemoryStream();
        CopyBounded(input, output, 32768, CancellationToken.None);
        var manifest = Deserialize<PluginManifest>(output.ToArray());
        PluginValidation.Validate(manifest);
        return manifest;
    }

    private Dictionary<string, string> Extract(ZipArchive zip, string directory, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith('/');
            var relative = PluginValidation.NormalizeRelativePath(isDirectory ? entry.FullName[..^1] : entry.FullName);
            if (!seen.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Duplicate paths or links are not allowed in packages.");
            if (Path.GetFileName(relative).Equals("WitchDrawer.PluginSdk.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Packages must use the host PluginSdk assembly.");
            var target = PackageFile(directory, relative);
            if (isDirectory) { EnsureSafeDirectory(target); continue; }
            expanded = checked(expanded + entry.Length);
            if (expanded > PluginValidation.MaxExpandedBytes) throw new InvalidDataException("Oversized expanded package.");
            EnsureSafeDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                CopyBounded(input, output, entry.Length, cancellationToken);
            using var file = File.OpenRead(target);
            files.Add(relative, Convert.ToHexString(SHA256.HashData(file)));
        }
        return files;
    }

    private static void CopyBounded(Stream input, Stream output, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count += read;
            if (count > limit) throw new InvalidDataException("Package exceeds its size limit.");
            output.Write(buffer, 0, read);
        }
    }

    private string PackageDirectory(string id, string generation)
    {
        PluginValidation.ValidateId(id);
        if (!Guid.TryParseExact(generation, "N", out _)) throw new InvalidDataException("Invalid package generation.");
        return Path.Combine(_root, "packages", id, generation);
    }

    private static string PackageFile(string directory, string relative)
    {
        PluginValidation.NormalizeRelativePath(relative);
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes its directory.");
        return path;
    }

    private void EnsureSafeDirectory(string directory)
    {
        EnsureInsideRoot(directory);
        EnsureNoReparsePoints(directory);
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(directory);
    }

    private void EnsureInsideRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (full != _root && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin operation escapes its storage directory.");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Plugin storage cannot contain links or reparse points.");
    }

    private void TryDeletePackage(InstalledPlugin plugin) => TryDeleteDirectory(PackageDirectory(plugin.Manifest.Id, plugin.Generation));
    private void TryDeleteDirectory(string directory)
    {
        EnsureInsideRoot(directory);
        try
        {
            if (!Directory.Exists(directory)) return;
            EnsureNoReparsePoints(directory);
            // Do not recurse through links introduced after installation.
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureNoReparsePoints(entry);
                if (Directory.Exists(entry)) TryDeleteDirectory(entry);
                else File.Delete(entry);
            }
            Directory.Delete(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _logger.Error(exception, "Plugin package cleanup deferred until the next management visit."); }
    }

    private async Task<byte[]> DownloadAsync(Uri url, long maxBytes, bool assetRedirects, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        idle.CancelAfter(_downloadIdleTimeout);
        try
        {
            for (var redirects = 0; redirects <= 5; redirects++)
            {
                var allowed = assetRedirects
                    ? url.Host is "github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com"
                    : url.AbsoluteUri == CatalogUrl;
                if (!allowed || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0)
                    throw new InvalidDataException("Unexpected plugin download redirect.");
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
                {
                    var location = response.Headers.Location ?? throw new HttpRequestException("Missing redirect URL.");
                    url = location.IsAbsoluteUri ? location : new Uri(url, location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Oversized plugin download.");
                await using var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
                using var result = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false)) != 0)
                {
                    if (result.Length + read > maxBytes) throw new InvalidDataException("Oversized plugin download.");
                    await result.WriteAsync(buffer.AsMemory(0, read), idle.Token).ConfigureAwait(false);
                    idle.CancelAfter(_downloadIdleTimeout);
                }
                return result.ToArray();
            }
            throw new HttpRequestException("Too many plugin download redirects.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Plugin download timed out.", exception);
        }
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
