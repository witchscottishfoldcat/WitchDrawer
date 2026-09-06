using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class ShellIconProviderTests
{
    [Theory]
    [InlineData(14)]
    [InlineData(25)]
    [InlineData(45)]
    public async Task GetIconAsync_ExistingExecutable_ReturnsRequestedPixelDimensions(int requestedSize)
    {
        var executablePath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(executablePath));

        var icon = await ShellIconProvider.GetIconAsync(
            executablePath,
            isDirectory: false,
            size: requestedSize);

        var bitmap = Assert.IsAssignableFrom<BitmapSource>(icon);
        Assert.Equal(requestedSize, bitmap.PixelWidth);
        Assert.Equal(requestedSize, bitmap.PixelHeight);
    }

    [Fact]
    public async Task GetIconAsync_LnkShortcut_PreservesTheConfiguredIconIndex()
    {
        var iconLibrary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "imageres.dll");
        Assert.True(File.Exists(iconLibrary));

        var targetPath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(targetPath));

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "WitchDrawer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var firstShortcut = Path.Combine(tempDirectory, "icon-0.lnk");
            var indexedShortcut = Path.Combine(tempDirectory, "icon-109.lnk");
            CreateShortcut(firstShortcut, targetPath, iconLibrary, 0);
            CreateShortcut(indexedShortcut, targetPath, iconLibrary, 109);

            var firstIcon = Assert.IsAssignableFrom<BitmapSource>(
                await ShellIconProvider.GetIconAsync(firstShortcut, isDirectory: false, size: 32));
            var indexedIcon = Assert.IsAssignableFrom<BitmapSource>(
                await ShellIconProvider.GetIconAsync(indexedShortcut, isDirectory: false, size: 32));

            Assert.False(GetPixels(firstIcon).SequenceEqual(GetPixels(indexedIcon)));
        }
        finally
        {
            TestCleanup.DeleteDirectory(tempDirectory);
        }
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string iconLibrary,
        int iconIndex)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        Assert.NotNull(shellType);

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            Assert.NotNull(shell);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            Assert.NotNull(shortcut);

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [targetPath]);
            shortcutType.InvokeMember(
                "IconLocation",
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [$"{iconLibrary},{iconIndex}"]);
            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static byte[] GetPixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
