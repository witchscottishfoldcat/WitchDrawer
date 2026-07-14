using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0.2/app.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123/abc", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/123/abc", true)]
    [InlineData("http://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0.2/app.zip", false)]
    [InlineData("https://evil.example/update.zip", false)]
    [InlineData("https://github.com/other/other/releases/download/v1.0.2/app.zip", false)]
    [InlineData("not-a-url", false)]
    public void IsAllowedDownloadUrl_FiltersUnexpectedHosts(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsAllowedDownloadUrl(url));
    }
}
