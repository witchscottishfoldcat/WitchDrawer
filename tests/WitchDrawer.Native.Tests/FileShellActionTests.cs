using System.Runtime.InteropServices;
using WitchDrawer.Native.Files;

namespace WitchDrawer.Native.Tests;

public sealed class FileShellActionTests
{
    [Fact]
    public async Task ShellWorker_DoesNotBlockCallerAndUsesSta()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<ApartmentState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = StaShellWorker.RunAsync(() =>
        {
            entered.SetResult(Thread.CurrentThread.GetApartmentState());
            release.Wait(); return true;
        });
        try
        {
            Assert.Equal(ApartmentState.STA, await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(work.IsCompleted);
        }
        finally { release.Set(); await work; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortcutTarget_ResolvesFileOrFolderWithoutLaunching(bool directory)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.ShortcutTargets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "target");
            if (directory) Directory.CreateDirectory(target); else File.WriteAllText(target, "payload");
            var shortcut = Path.Combine(root, "shortcut.lnk");
            await StaShellWorker.RunAsync(() =>
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
                object? link = null;
                try
                {
                    link = shell.CreateShortcut(shortcut);
                    ((dynamic)link).TargetPath = target;
                    ((dynamic)link).Save();
                }
                finally
                {
                    if (link is not null) Marshal.FinalReleaseComObject(link);
                    Marshal.FinalReleaseComObject((object)shell);
                }
                return true;
            });
            Assert.Equal(target, await WindowsFileShellActions.GetShortcutTargetAsync(shortcut));
            Assert.True(File.Exists(shortcut));
            if (!directory) Assert.Equal("payload", File.ReadAllText(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WebShortcut_DoesNotPretendToHaveLocalFileLocation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WitchDrawer.WebShortcut-{Guid.NewGuid():N}.url");
        try
        {
            File.WriteAllText(path, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
            await Assert.ThrowsAsync<IOException>(() => WindowsFileShellActions.GetShortcutTargetAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(@"C:\folder")]
    [InlineData(@"C:\folder\file.txt")]
    public void Reveal_SelectsItemInParentForFilesAndFolders(string path)
    {
        var start = WindowsFileShellActions.CreateRevealStartInfo(path);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.Equal($"/select,\"{path}\"", start.Arguments);
    }
}
