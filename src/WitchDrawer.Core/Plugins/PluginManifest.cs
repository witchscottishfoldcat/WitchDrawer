using System.Text.Json;

namespace WitchDrawer.Core.Plugins;

public sealed record PluginManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public int ApiVersion { get; init; } = 1;
    public string MinHostVersion { get; init; } = "1.4.4";
    public string? MaxHostVersion { get; init; }
    public string RepositoryUrl { get; init; } = "";
    public string EntryAssembly { get; init; } = "";
    public string EntryType { get; init; } = "";
    public string[] Permissions { get; init; } = [];
}

public sealed record PluginCatalogEntry
{
    public PluginManifest Plugin { get; init; } = new();
    public string PackageUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string? ScreenshotUrl { get; init; }
}

public sealed record PluginCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public List<PluginCatalogEntry> Plugins { get; init; } = [];
}

public sealed record InstalledPlugin
{
    public PluginManifest Manifest { get; init; } = new();
    public string Generation { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public bool Enabled { get; init; }
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Dictionary<string, string> Settings { get; init; } = [];
    public Dictionary<string, string> Files { get; init; } = [];
}

internal sealed record PluginState
{
    public int SchemaVersion { get; init; } = 1;
    public List<InstalledPlugin> Plugins { get; init; } = [];
}

public static class PluginJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}
