using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

internal static class DesktopWindowLayerPolicy
{
    // Cache the decision so window messages never query the Windows edition.
    internal static bool IsEnabled { get; } = DetectCompatibilityPath();

    internal static bool MatchesReportedSystem(Version version, uint productType) =>
        IsWindows11Version23H2(version)
        && productType is 0x000000A1 or 0x000000A2; // PRODUCT_PRO_WORKSTATION / _N

    private static bool IsWindows11Version23H2(Version version) =>
        version.Major == 10 && version.Minor == 0 && version.Build == 22631;

    private static bool DetectCompatibilityPath()
    {
        var version = Environment.OSVersion.Version;
        if (!OperatingSystem.IsWindows() || !IsWindows11Version23H2(version))
        {
            return false;
        }

        // Use the native SKU instead of a localized edition name. Query failure
        // keeps the existing ownership path; monthly update revisions are irrelevant.
        return GetProductInfo(10, 0, 0, 0, out var productType)
            && MatchesReportedSystem(version, productType);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProductInfo(
        uint majorVersion, uint minorVersion, uint servicePackMajor,
        uint servicePackMinor, out uint productType);
}
