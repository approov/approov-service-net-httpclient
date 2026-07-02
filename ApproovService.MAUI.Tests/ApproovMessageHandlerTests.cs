using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovMessageHandlerTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.FetchCallCount = 0;
        ApproovService.NextFetchResult = null;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public async Task SendAsync_BypassMode_RequestPassesThrough()
    {
        ApproovService.Initialize("");
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner);
        var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_Initialized_TokenHeaderAdded()
    {
        ApproovService.Initialize("dummy-config");
        HttpRequestMessage? captured = null;
        var inner = new FakeHandler(HttpStatusCode.OK, req => { captured = req; });
        var handler = new ApproovMessageHandler(inner);
        var client = new HttpClient(handler);
        await client.GetAsync("https://example.com");
        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public async Task SendAsync_ShouldRetryDecision_ReturnsServiceUnavailable()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner);
        var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ShouldFailDecision_ThrowsApproovException()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Rejected, ARC = "ARC1", RejectionReasons = "r1" };
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner);
        var client = new HttpClient(handler);
        await Assert.ThrowsAsync<RejectionException>(() => client.GetAsync("https://example.com"));
    }

    [Fact]
    public void ApproovHttpClient_DefaultCtor_UsesApproovMessageHandler()
    {
        var client = new ApproovHttpClient();
        Assert.NotNull(client);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Action<HttpRequestMessage>? _capture;
        internal FakeHandler(HttpStatusCode status, Action<HttpRequestMessage>? capture = null)
        {
            _status = status; _capture = capture;
        }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            _capture?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
