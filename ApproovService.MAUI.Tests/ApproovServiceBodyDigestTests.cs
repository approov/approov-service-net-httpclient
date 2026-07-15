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
    public async Task BodyDigest_ExistingHeader_IsReplacedInsteadOfDuplicated()
    {
        ApproovService.Initialize("");
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("current-body")
        };
        req.Content.Headers.TryAddWithoutValidation(
            "Content-Digest", "sha-256=:c3RhbGU=:");

        await client.SendAsync(req);

        string[] values = req.Content.Headers.GetValues("Content-Digest").ToArray();
        Assert.Single(values);
        Assert.DoesNotContain("c3RhbGU=", values[0]);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task BodyDigest_DocumentedBodyMethods_AddContentDigestHeader(string method)
    {
        ApproovService.Initialize("");
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(new HttpMethod(method), "https://example.com/api")
        {
            Content = new StringContent("body")
        };

        await client.SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
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

    [Fact]
    public async Task BodyDigest_DefaultMode_NonSeekableBody_ProceedsWithoutDigest()
    {
        ApproovService.Initialize("");
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 }))
        };

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_NonSeekableBody_ThrowsAndDoesNotSend()
    {
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 }))
        };

        await Assert.ThrowsAsync<PermanentException>(() => client.SendAsync(req));
        Assert.False(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_RepeatableBody_AddsContentDigestHeader()
    {
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var handler = new ApproovMessageHandler(new NoOpHandler());
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("{\"key\":\"value\"}")
        };

        await client.SendAsync(req);

        Assert.True(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_NoBody_ProceedsWithoutError()
    {
        // Documented choice: required mode fails closed only when there is a body
        // that cannot be digested; no body means there is nothing to digest.
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_RequiredMode_NonBodyMethod_ProceedsWithoutError()
    {
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_DisabledWithRequired_DisabledWins()
    {
        // required is only meaningful when the digest is enabled
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(false, required: true);
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 }))
        };

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
        Assert.False(req.Content!.Headers.Contains("Content-Digest"));
    }

    [Fact]
    public async Task BodyDigest_SingleArgOverload_ClearsRequiredMode()
    {
        ApproovService.Initialize("");
        ApproovService.SetBodyDigestEnabled(true, required: true);
        ApproovService.SetBodyDigestEnabled(true);
        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 }))
        };

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    [Fact]
    public async Task BodyDigest_Reinitialize_ResetsRequiredModeToDefault()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBodyDigestEnabled(true, required: true);

        // A successful (re)initialization resets configurable state to defaults
        ApproovService.Initialize("dummy-config");

        var inner = new NoOpHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 }))
        };

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        public bool Reached { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Reached = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    // A read-once stream: no length, no seeking — models one-shot streaming content
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
        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, System.IO.SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
