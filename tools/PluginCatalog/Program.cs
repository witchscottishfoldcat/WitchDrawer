using System.Text.Json;
using WitchDrawer.Core.Plugins;

try
{
    var root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Directory.GetCurrentDirectory();
    var directory = Path.Combine(Path.GetFullPath(root), "plugins");
    var catalog = new PluginCatalog();
    foreach (var path in Directory.EnumerateFiles(Path.Combine(directory, "entries"), "*.json").Order(StringComparer.Ordinal))
    {
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("Oversized entry: " + path);
        var entry = JsonSerializer.Deserialize<PluginCatalogEntry>(File.ReadAllBytes(path), PluginJson.Options)
            ?? throw new InvalidDataException("Invalid entry: " + path);
        if (Path.GetFileNameWithoutExtension(path) != entry.Plugin.Id) throw new InvalidDataException("Entry filename must equal its plugin id: " + path);
        catalog.Plugins.Add(entry);
    }
    PluginValidation.Validate(catalog);
    var catalogPath = Path.Combine(directory, "catalog.json");
    if (args.Contains("--write"))
    {
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, PluginJson.Options) + "\n");
        Console.WriteLine($"Generated catalog: {catalog.Plugins.Count} plugin(s).");
    }
    else
    {
        var current = JsonSerializer.Deserialize<PluginCatalog>(File.ReadAllBytes(catalogPath), PluginJson.Options)
            ?? throw new InvalidDataException("Invalid catalog.");
        PluginValidation.Validate(current);
        if (JsonSerializer.Serialize(current, PluginJson.Options) != JsonSerializer.Serialize(catalog, PluginJson.Options))
            throw new InvalidDataException("catalog.json is out of date. Run dotnet run --project tools/PluginCatalog -- --write.");
        Console.WriteLine($"Catalog valid: {catalog.Plugins.Count} plugin(s). No plugin code was executed.");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
