namespace WitchDrawer.App.Infrastructure;

internal static class StartupLaunchPolicy
{
    internal const string SilentArgument = "--silent";

    public static bool IsSilent(IEnumerable<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, SilentArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldActivateExistingInstance(IEnumerable<string> arguments)
    {
        return !IsSilent(arguments);
    }
}
