using System.Text.RegularExpressions;

namespace WitchDrawer.Core.Plugins;

/// <summary>Shared by installation and the catalog validator; never executes plugin code.</summary>
public static partial class PluginValidation
{
    public const int ApiVersion = 1;
    public const long MaxPackageBytes = 64 * 1024 * 1024;
    public const long MaxExpandedBytes = 128 * 1024 * 1024;
    public const int MaxFiles = 4096;
    public const int MaxCatalogBytes = 2 * 1024 * 1024;

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$")]
    private static partial Regex IdPattern();
    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex HashPattern();

    public static void ValidateId(string id)
    {
        if (id is null || id.Length > 100 || !IdPattern().IsMatch(id)
            || new[] { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" }.Contains(id.Split('.')[0]))
            throw new InvalidDataException("Invalid plugin id.");
    }

    public static Version ParseVersion(string value)
    {
        if (value is null || value.Length > 40 || !VersionPattern().IsMatch(value) || !Version.TryParse(value, out var version))
            throw new InvalidDataException("Versions must use X.Y.Z.");
        return version;
    }

    public static void ValidateHash(string hash)
    {
        if (hash is null || !HashPattern().IsMatch(hash)) throw new InvalidDataException("Invalid SHA-256.");
    }

    public static void Validate(PluginManifest manifest)
    {
        if (manifest is null) throw new InvalidDataException("Missing plugin manifest.");
        ValidateId(manifest.Id);
        ParseVersion(manifest.Version);
        var min = ParseVersion(manifest.MinHostVersion);
        if (manifest.MaxHostVersion is { } max && ParseVersion(max) < min)
            throw new InvalidDataException("Invalid host version range.");
        if (manifest.ApiVersion != ApiVersion) throw new InvalidDataException("Unsupported plugin API.");
        foreach (var text in new[] { manifest.Name, manifest.Author, manifest.Description, manifest.EntryType })
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new InvalidDataException("Missing or oversized plugin metadata.");
        var repository = RequireHttps(manifest.RepositoryUrl);
        if (repository.Host != "github.com" || repository.Query.Length != 0 || repository.Fragment.Length != 0
            || repository.AbsolutePath.Trim('/').Split('/').Length != 2)
            throw new InvalidDataException("A GitHub source repository is required.");
        var assembly = NormalizeRelativePath(manifest.EntryAssembly);
        if (assembly != manifest.EntryAssembly || !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Entry assembly must be a relative DLL path.");
        if (manifest.Permissions is null || manifest.Permissions.Distinct().Count() != manifest.Permissions.Length
            || manifest.Permissions.Any(p => p is not ("network" or "system-metrics" or "files")))
            throw new InvalidDataException("Unknown or duplicate declared capabilities.");
    }

    public static bool IsCompatible(PluginManifest manifest, Version hostVersion)
    {
        var host = new Version(hostVersion.Major, hostVersion.Minor, Math.Max(0, hostVersion.Build));
        return manifest.ApiVersion == ApiVersion && host >= ParseVersion(manifest.MinHostVersion)
            && (manifest.MaxHostVersion is null || host <= ParseVersion(manifest.MaxHostVersion));
    }

    public static void Validate(PluginCatalog catalog)
    {
        if (catalog is null || catalog.SchemaVersion != 1 || catalog.Plugins is null || catalog.Plugins.Count > 1000)
            throw new InvalidDataException("Unsupported or oversized plugin catalog.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Plugins)
        {
            if (entry is null) throw new InvalidDataException("Null catalog entry.");
            Validate(entry.Plugin);
            if (!ids.Add(entry.Plugin.Id)) throw new InvalidDataException("Duplicate plugin id.");
            ValidateHash(entry.Sha256);
            var package = RequireHttps(entry.PackageUrl);
            var repository = RequireHttps(entry.Plugin.RepositoryUrl);
            var prefix = repository.AbsolutePath.TrimEnd('/') + "/releases/download/";
            if (package.Host != "github.com" || !package.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || package.AbsolutePath[prefix.Length..].Split('/').Length != 2
                || package.AbsolutePath.Contains("/latest/", StringComparison.OrdinalIgnoreCase)
                || !package.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || package.Query.Length != 0 || package.Fragment.Length != 0)
                throw new InvalidDataException("Package URL must pin a GitHub Release asset in the source repository.");
            if (entry.ScreenshotUrl is { } screenshot)
            {
                var uri = RequireHttps(screenshot);
                if (uri.Host is not ("github.com" or "raw.githubusercontent.com"))
                    throw new InvalidDataException("Screenshots must be hosted on GitHub.");
            }
        }
    }

    public static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidDataException("An HTTPS URL without credentials is required.");
        return uri;
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
            throw new InvalidDataException("Invalid package path.");
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || part.Any(char.IsControl))
                throw new InvalidDataException("Invalid package path.");
            var stem = part.Split('.')[0].ToLowerInvariant();
            if (stem is "con" or "prn" or "aux" or "nul" || (stem.Length == 4 && (stem.StartsWith("com") || stem.StartsWith("lpt")) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException("Reserved package path.");
        }
        return path;
    }
}
