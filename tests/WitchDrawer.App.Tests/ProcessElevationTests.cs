using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class ProcessElevationTests
{
    [Fact]
    public void RequiresUnelevatedRelaunch_CanReadCurrentProcessToken()
    {
        _ = ProcessElevation.RequiresUnelevatedRelaunch();
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say\"hello", "\"say\\\"hello\"")]
    [InlineData("C:\\folder with spaces\\", "\"C:\\folder with spaces\\\\\"")]
    public void QuoteCommandLineArgument_UsesWindowsEscaping(
        string argument,
        string expected)
    {
        Assert.Equal(expected, ProcessElevation.QuoteCommandLineArgument(argument));
    }

    [Fact]
    public void BuildCommandLine_PreservesExecutableAndEveryArgument()
    {
        var commandLine = ProcessElevation.BuildCommandLine(
            @"C:\Program Files\WitchDrawer\WitchDrawer.App.exe",
            ["--silent", "two words"]);

        Assert.Equal(
            "\"C:\\Program Files\\WitchDrawer\\WitchDrawer.App.exe\" --silent \"two words\"",
            commandLine);
    }
}
