using System.Diagnostics;

namespace WitchDrawer.Core.Tests;

public sealed class ReleasePackagingTests
{
    [Fact]
    public async Task Packaging_RejectsMismatchedVersionBeforeChangingPublishDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "WitchDrawer.sln"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.PackagingTests", Guid.NewGuid().ToString("N"));
        var tools = Path.Combine(root, "tools");
        var publish = Path.Combine(root, "publish", "v9.0.0");
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(publish);
        try
        {
            var script = Path.Combine(tools, "Publish-WitchDrawer.ps1");
            File.Copy(Path.Combine(repository.FullName, "tools", "Publish-WitchDrawer.ps1"), script);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup><Version>1.3.15</Version></PropertyGroup></Project>");
            var sentinel = Path.Combine(publish, "existing.txt");
            await File.WriteAllTextAsync(sentinel, "preserve");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-File", script, "-Version", "9.0.0" }) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("does not match", await stderr);
            Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel));
            Assert.DoesNotContain("dotnet publish", await stdout);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
