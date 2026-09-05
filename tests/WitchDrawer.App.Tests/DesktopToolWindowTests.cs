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

    [Theory]
    [InlineData("WorkerW", true, true)]
    [InlineData("WorkerW", false, false)]
    [InlineData("Progman", true, false)]
    [InlineData("SHELLDLL_DefView", true, false)]
    [InlineData(null, true, false)]
    public void IsDesktopHostCandidate_RequiresWorkerWithShellDefView(
        string? className,
        bool containsShellDefView,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopToolWindow.IsDesktopHostCandidate(className, containsShellDefView));
    }

    [Fact]
    public void Configure_ClearsExistingOwner()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? owner = null;
            Window? window = null;
            try
            {
                owner = CreateTestWindow();
                window = CreateTestWindow();
                var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
                var handle = new WindowInteropHelper(window).EnsureHandle();
                SetWindowLongPtr(handle, WindowOwnerIndex, ownerHandle);
                Assert.Equal(ownerHandle, GetWindow(handle, GetWindowOwner));

                var nativeWindow = new DesktopToolWindow(handle);
                nativeWindow.Configure();

                Assert.Equal(nint.Zero, GetWindow(handle, GetWindowOwner));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                owner?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "STA window test timed out.");
        Assert.Null(failure);
    }

    private static Window CreateTestWindow() =>
        new()
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            Width = 1,
            Height = 1
        };

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value)
    {
        return nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : SetWindowLong32(windowHandle, index, value);
    }

    private const int WindowOwnerIndex = -8;
    private const uint GetWindowOwner = 4;

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);
}
