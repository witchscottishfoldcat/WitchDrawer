using System.Net;
using System.Text;
using System.Text.Json;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.Core.Tests;

public sealed class UpdateCheckTests
{
    private static readonly Version CurrentVersion = new(1, 3, 15);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CheckForUpdateAsync_HttpFailureDoesNotReturnNoUpdate(HttpStatusCode statusCode)
    {
        using var client = CreateClient(statusCode, "{}");
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CheckForUpdateAsync(CurrentVersion));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ConnectionFailurePropagatesOriginalException()
    {
        var failure = new HttpRequestException("The network is unavailable.");
        using var client = new HttpClient(new ResponseHandler((_, _) => throw failure));
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CheckForUpdateAsync(CurrentVersion));

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public async Task CheckForUpdateAsync_InvalidJsonDoesNotReturnNoUpdate(string body)
    {
        using var client = CreateClient(HttpStatusCode.OK, body);
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);

        await Assert.ThrowsAsync<JsonException>(() => service.CheckForUpdateAsync(CurrentVersion));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"\"}")]
    [InlineData("{\"tag_name\":\"   \"}")]
    [InlineData("{\"tag_name\":\"latest\"}")]
    public async Task CheckForUpdateAsync_MissingOrInvalidVersionDoesNotReturnNoUpdate(string body)
    {
        using var client = CreateClient(HttpStatusCode.OK, body);
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync(CurrentVersion));
    }

    [Theory]
    [InlineData("v1.3.15", false)]
    [InlineData("v1.3.14", false)]
    [InlineData("v1.3.16", true)]
    public async Task CheckForUpdateAsync_ValidReleaseReturnsVersionComparison(string tag, bool hasUpdate)
    {
        using var client = CreateClient(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { tag_name = tag, body = "Release notes", assets = Array.Empty<object>() }));
        var service = new UpdateService(NullAppLogger.Instance, httpClient: client);

        var result = await service.CheckForUpdateAsync(CurrentVersion);

        Assert.Equal(hasUpdate, result.HasUpdate);
        Assert.Equal(Version.Parse(tag[1..]), result.LatestVersion);
        Assert.Equal("Release notes", result.ReleaseNotes);
        Assert.Equal("https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest", result.DownloadUrl);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string body) =>
        new(new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })));

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
