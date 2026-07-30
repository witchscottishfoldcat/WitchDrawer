using System.Reflection;
using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

public static class StartupShortcutMigration
{
    private const string ShortcutFileName = "WitchDrawer.lnk";

    public static StartupShortcutMigrationResult EnsureSilentArguments(
        string? executablePath,
        string silentArgument)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return StartupShortcutMigrationResult.Empty;
        }

        var updatedCount = 0;
        var errors = new List<Exception>();

        foreach (var shortcutPath in GetStartupShortcutPaths())
        {
            try
            {
                if (File.Exists(shortcutPath)
                    && EnsureSilentArgument(shortcutPath, executablePath, silentArgument))
                {
                    updatedCount++;
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        return new StartupShortcutMigrationResult(updatedCount, errors);
    }

    private static IEnumerable<string> GetStartupShortcutPaths()
    {
        var startupFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        };

        return startupFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(path, ShortcutFileName))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool EnsureSilentArgument(
        string shortcutPath,
        string executablePath,
        string silentArgument)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return false;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                return false;
            }

            var shortcutType = shortcut.GetType();
            var targetPath = shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string;

            if (!PathsEqual(targetPath, executablePath))
            {
                return false;
            }

            var arguments = shortcutType.InvokeMember(
                "Arguments",
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string ?? string.Empty;

            if (ContainsArgument(arguments, silentArgument))
            {
                return false;
            }

            var updatedArguments = string.IsNullOrWhiteSpace(arguments)
                ? silentArgument
                : $"{arguments.Trim()} {silentArgument}";
            shortcutType.InvokeMember(
                "Arguments",
                BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [updatedArguments]);
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
            return true;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static bool PathsEqual(string? first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsArgument(string arguments, string expectedArgument)
    {
        return arguments
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(argument => string.Equals(
                argument.Trim('"'),
                expectedArgument,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

public sealed record StartupShortcutMigrationResult(
    int UpdatedCount,
    IReadOnlyList<Exception> Errors)
{
    public static StartupShortcutMigrationResult Empty { get; } = new(0, []);
}
