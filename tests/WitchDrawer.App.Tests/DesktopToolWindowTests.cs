using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    [Fact]
    public void Configure_AssignsProgmanAsOwner()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Width = 1,
                    Height = 1
                };
                var handle = new WindowInteropHelper(window).EnsureHandle();
                var shellWindow = GetShellWindow();
                var nativeWindow = new DesktopToolWindow(handle);

                nativeWindow.Configure();

                Assert.NotEqual(nint.Zero, shellWindow);
                Assert.Equal(shellWindow, GetWindow(handle, GetWindowOwner));
                Assert.True(nativeWindow.IsDesktopHosted);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA window test timed out.");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public void IsShowDesktopShortcut_RequiresDAndEitherWindowsKey(
        bool dKeyDown,
        bool leftWindowsKeyDown,
        bool rightWindowsKeyDown,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.IsShowDesktopShortcut(
                dKeyDown,
                leftWindowsKeyDown,
                rightWindowsKeyDown));
    }

    [Theory]
    [InlineData(10, 10, true, 42, 42, false)]
    [InlineData(10, 20, false, 0, 42, true)]
    [InlineData(10, 20, true, 42, 42, true)]
    [InlineData(10, 20, true, 99, 42, false)]
    [InlineData(0, 20, false, 0, 42, false)]
    [InlineData(10, 0, false, 0, 42, false)]
    public void ShouldRepairShellLastActivePopup_RepairsOnlyStaleOrCurrentProcessEntries(
        long shellWindow,
        long lastActivePopup,
        bool popupIsValid,
        uint popupProcessId,
        uint currentProcessId,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.ShouldRepairShellLastActivePopup(
                (nint)shellWindow,
                (nint)lastActivePopup,
                popupIsValid,
                popupProcessId,
                currentProcessId));
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

    [Fact]
    public void MouseActivationResult_DeliversClickWithoutActivatingBox()
    {
        Assert.Equal((nint)3, DesktopToolWindow.GetMouseActivateWithoutActivationResult());
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

    private const uint GetWindowOwner = 4;

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();
}
