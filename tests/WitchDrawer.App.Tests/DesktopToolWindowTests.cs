using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopToolWindowTests
{
    [Fact]
    public void TaskbarCreatedMessage_IsRegistered()
    {
        Assert.NotEqual(0, DesktopToolWindow.TaskbarCreatedMessage);
    }

    [Theory]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF020, true)]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF023, true)]
    [InlineData(DesktopToolWindow.SystemCommandMessage, 0xF060, false)]
    [InlineData(0x0111, 0xF020, false)]
    public void IsMinimizeSystemCommand_RecognizesOnlySystemMinimize(
        int message,
        long command,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.IsMinimizeSystemCommand(message, (nint)command));
    }

    [Theory]
    [InlineData(0x0021, true)]
    [InlineData(0x0201, false)]
    [InlineData(0x0112, false)]
    public void IsMouseActivationMessage_RecognizesOnlyMouseActivate(
        int message,
        bool expected)
    {
        Assert.Equal(expected, DesktopToolWindow.IsMouseActivationMessage(message));
    }

    [Theory]
    [InlineData(0x001F, true)]
    [InlineData(0x00A2, true)]
    [InlineData(0x00A5, true)]
    [InlineData(0x00A8, true)]
    [InlineData(0x00AC, true)]
    [InlineData(0x0202, true)]
    [InlineData(0x0205, true)]
    [InlineData(0x0208, true)]
    [InlineData(0x020C, true)]
    [InlineData(0x0232, true)]
    [InlineData(0x0201, false)]
    [InlineData(0x0021, false)]
    public void IsMouseInteractionCompletionMessage_RecognizesReleaseAndMoveCompletion(
        int message,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.IsMouseInteractionCompletionMessage(message));
    }
}
