using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using WitchDrawer.Core.Plugins;
using WitchDrawer.PluginSdk;

namespace WitchDrawer.App.Plugins;

internal sealed class PluginLoadContext(string entryPath, InstalledPlugin installed) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);
    private readonly string _directory = FindPackageRoot(entryPath, installed.Manifest.EntryAssembly);

    private static string FindPackageRoot(string path, string relative)
    {
        var directory = Path.GetDirectoryName(path)!;
        foreach (var _ in relative.Where(c => c == '/')) directory = Path.GetDirectoryName(directory)!;
        return directory;
    }

    // The SDK must have the same type identity even when the host is published as a single file.
    public Assembly LoadEntry(string path)
    {
        ValidateDependency(path);
        // Keep managed packages replaceable even when WPF caches delay collectible-context GC.
        using var input = File.OpenRead(path);
        return LoadFromStream(input);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(IWidgetPlugin).Assembly.GetName().Name) return typeof(IWidgetPlugin).Assembly;
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path is null) return null;
        ValidateDependency(path);
        return LoadEntry(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        if (path is null) return 0;
        ValidateDependency(path);
        return LoadUnmanagedDllFromPath(path);
    }

    private void ValidateDependency(string path)
    {
        var relative = Path.GetRelativePath(_directory, path).Replace('\\', '/');
        PluginValidation.NormalizeRelativePath(relative);
        if (!installed.Files.ContainsKey(relative)) throw new InvalidDataException("Dependency is not part of the installed package.");
    }
}
