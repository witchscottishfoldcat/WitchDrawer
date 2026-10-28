using System.Diagnostics;

namespace WitchDrawer.Native.Shell;

public static class ExternalLinkLauncher
{
    public static Task OpenAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new ArgumentException("An HTTPS URL is required.", nameof(url));
        return Task.Run(() => { using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); });
    }
}
