using System.IO;
using System.Text.RegularExpressions;

namespace WitchDrawer.App.Tests;

public sealed class InstallerSafetyTests
{
    [Fact]
    public void Uninstall_PreservesUnownedFilesAndDiscardsLegacyRecursiveDeletionLog()
    {
        var script = LoadScript();
        var section = Regex.Match(script, @"(?ms)^\[UninstallDelete\]\s*(.*?)(?=^\[|\z)").Groups[1].Value;
        var entries = section.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith(';')).ToArray();
        Assert.Single(entries);
        Assert.Contains("Type: dirifempty; Name: \"{app}\"", entries[0]);
        Assert.Contains("UninstallLogMode=overwrite", script);
    }

    [Fact]
    public void Startup_UsesRuntimeRunEntryAndOnlyUninstallsItsOwnRegistration()
    {
        var script = LoadScript();
        Assert.DoesNotContain("Name: \"{autostartup}", script);
        Assert.Contains("ValueName: \"WitchDrawer\"", script);
        Assert.Contains("Software\\Microsoft\\Windows\\CurrentVersion\\Run", script);
        Assert.DoesNotContain("uninsdeletevalue", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExecutablePrefix := '\"' + ExpandConstant('{app}\\{#MyAppExeName}') + '\"'", script);
        Assert.Contains("CompareText(StartupCommand, ExecutablePrefix)", script);
        Assert.Contains("CompareText(Shortcut.TargetPath, ExpandConstant('{app}\\{#MyAppExeName}'))", script);
    }

    private static string LoadScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), "installer", "WitchDrawer.iss"));
    }
}
