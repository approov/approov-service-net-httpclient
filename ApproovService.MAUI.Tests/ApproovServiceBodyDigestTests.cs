using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceBodyDigestTests : IDisposable
{
    public ApproovServiceBodyDigestTests()
    {
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    public void Dispose()
    {
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    [Fact]
    public async Task BodyDigest_BypassPost_RemainsUnchanged()
    {
        ApproovService.Initialize("");
        var req = PostWith(new StringContent("{\"key\":\"value\"}"));

        await SendAsync(req);

        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
        Assert.Equal(0, ApproovService.InstallSignatureCallCount);
    }

    [Theory]
    [InlineData(ApproovTokenFetchStatus.UnknownUrl)]
    [InlineData(ApproovTokenFetchStatus.UnprotectedUrl)]
    public async Task BodyDigest_UnprotectedPost_RemainsUnchanged(
        ApproovTokenFetchStatus status)
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = status };
        var req = PostWith(new StringContent("{\"key\":\"value\"}"));

        await SendAsync(req);

        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
        Assert.Equal(0, ApproovService.InstallSignatureCallCount);
    }

    [Fact]
    public async Task BodyDigest_ProtectedPost_DefaultSignerAddsContentDigestHeader()
    {
        ApproovService.Initialize("dummy-config");
        var req = PostWith(new StringContent("{\"key\":\"value\"}"));

        await SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
        string digest = string.Join("", req.Content.Headers.GetValues("Content-Digest"));
        Assert.StartsWith("sha-256=:", digest);
        Assert.EndsWith(":", digest);
        Assert.Equal(1, ApproovService.InstallSignatureCallCount);
    }

    [Fact]
    public async Task BodyDigest_ExistingHeader_IsReplacedInsteadOfDuplicated()
    {
        ApproovService.Initialize("dummy-config");
        var req = PostWith(new StringContent("current-body"));
        req.Content!.Headers.TryAddWithoutValidation(
            "Content-Digest", "sha-256=:c3RhbGU=:");

        await SendAsync(req);

        string[] values = req.Content.Headers.GetValues("Content-Digest").ToArray();
        Assert.Single(values);
        Assert.DoesNotContain("c3RhbGU=", values[0]);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task BodyDigest_ProtectedReplayableBody_AddsContentDigestHeader(string method)
    {
        ApproovService.Initialize("dummy-config");
        var req = new HttpRequestMessage(new HttpMethod(method), "https://example.com/api")
        {
            Content = new StringContent("body")
        };

        await SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_GetRequest_NoContentDigestHeader()
    {
        ApproovService.Initialize("dummy-config");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        await SendAsync(req);

        Assert.False(req.Content?.Headers.Contains("Content-Digest") ?? false);
    }

    [Fact]
    public async Task BodyDigest_Disabled_ProtectedPostHasNoContentDigestHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(false);
        var req = PostWith(new StringContent("hello"));

        await SendAsync(req);

        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_MemoryStreamBody_IsDigested()
    {
        ApproovService.Initialize("dummy-config");
        var req = PostWith(new StreamContent(
            new System.IO.MemoryStream(new byte[] { 1, 2, 3 })));

        await SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_DefaultMode_NonSeekableBody_ProceedsWithoutDigest()
    {
        ApproovService.Initialize("dummy-config");
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        var response = await SendAsync(req, inner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_NonSeekableBody_FailsAndDoesNotSend()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(req, inner));

        Assert.False(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_RepeatableBody_AddsContentDigestHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var req = PostWith(new StringContent("{\"key\":\"value\"}"));

        await SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_NoBody_FailsClosed()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var inner = new NoOpHandler();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(req, inner));

        Assert.False(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_DisabledWithRequired_DisabledWins()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(false, required: true);
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        var response = await SendAsync(req, inner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_SingleArgOverload_ClearsRequiredMode()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        ApproovService.SetBodyDigestEnabled(true);
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        var response = await SendAsync(req, inner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_SameConfigReinitialize_RestoresOptionalDefault()
    {
        // A same-config re-initialization is an initialization boundary, so required-digest
        // mode is reset along with the rest of the runtime configuration. Matches React
        // Native, which resets on every initialize regardless of configuration equality.
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        ApproovService.Initialize("dummy-config");
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        await SendAsync(req, inner);

        Assert.True(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_DifferentConfigReinitialize_RestoresOptionalDefault()
    {
        ApproovService.Initialize("config-a");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        ApproovService.Initialize("config-b");
        var inner = new NoOpHandler();
        var req = PostWith(new StreamContent(
            new NonSeekableStream(new byte[] { 1, 2, 3 })));

        var response = await SendAsync(req, inner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    private static HttpRequestMessage PostWith(HttpContent content) =>
        new(HttpMethod.Post, "https://example.com/api") { Content = content };

    private static async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, NoOpHandler? inner = null)
    {
        inner ??= new NoOpHandler();
        using var handler = new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true);
        using var client = new HttpClient(handler);
        return await client.SendAsync(request);
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        public bool Reached { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Reached = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request });
        }
    }

    private sealed class NonSeekableStream : System.IO.Stream
    {
        private readonly System.IO.MemoryStream _inner;
        public NonSeekableStream(byte[] data) => _inner = new System.IO.MemoryStream(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);
        public override long Seek(long offset, System.IO.SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
