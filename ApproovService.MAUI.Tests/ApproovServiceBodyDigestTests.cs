// ApproovService.MAUI.Tests/ApproovServiceBodyDigestTests.cs
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceBodyDigestTests : IDisposable
{
    public void Dispose() => ApproovService.ResetForTesting();

    [Fact]
    public async Task BodyDigest_PostRequest_AddsContentDigestHeader()
    {
        ApproovService.Initialize("");
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("{\"key\":\"value\"}")
        };
        await client.SendAsync(req);
        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
        string digest = string.Join("", req.Content.Headers.GetValues("Content-Digest"));
        Assert.StartsWith("sha-256=:", digest);
        Assert.EndsWith(":", digest);
    }

    [Fact]
    public async Task BodyDigest_GetRequest_NoContentDigestHeader()
    {
        ApproovService.Initialize("");
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        await client.SendAsync(req);
        Assert.False(req.Content?.Headers.Contains("Content-Digest") ?? false);
    }

    [Fact]
    public async Task BodyDigest_Disabled_PostHasNoContentDigestHeader()
    {
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(false);
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("hello")
        };
        await client.SendAsync(req);
        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_MemoryStreamBody_DoesNotThrow()
    {
        ApproovService.Initialize("");
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var stream = new System.IO.MemoryStream(new byte[] { 1, 2, 3 });
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(stream)
        };
        var ex = await Record.ExceptionAsync(() => client.SendAsync(req));
        Assert.Null(ex);
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
