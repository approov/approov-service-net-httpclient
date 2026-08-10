using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovMessageHandlerTests : IDisposable
{
    private static readonly HttpRequestOptionsKey<string> CorrelationOption =
        new("Approov.Tests.Correlation");

    public ApproovMessageHandlerTests()
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
    public async Task SendAsync_BypassMode_RequestPassesThrough()
    {
        ApproovService.Initialize("");
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Constructor_CustomHttpClientHandler_InstallsPinningCallback()
    {
        // Previously only the parameterless constructor wired pinning, so a caller-supplied
        // handler produced tokenized, signed requests over an unpinned connection.
        var inner = new HttpClientHandler();
        Assert.Null(inner.ServerCertificateCustomValidationCallback);

        _ = new ApproovMessageHandler(inner);

        Assert.NotNull(inner.ServerCertificateCustomValidationCallback);
        Assert.False(inner.AllowAutoRedirect);
    }

    [Fact]
    public void Constructor_CustomSocketsHttpHandler_InstallsPinningCallback()
    {
        // SocketsHttpHandler validates through SslOptions rather than the request-aware
        // callback, so it previously received no pinning at all.
        var inner = new SocketsHttpHandler();
        Assert.Null(inner.SslOptions.RemoteCertificateValidationCallback);

        _ = new ApproovMessageHandler(inner);

        Assert.NotNull(inner.SslOptions.RemoteCertificateValidationCallback);
        Assert.False(inner.AllowAutoRedirect);
    }

    [Fact]
    public void Constructor_CustomSocketsHttpHandler_KeepsCallerSuppliedCallback()
    {
        System.Net.Security.RemoteCertificateValidationCallback callerCallback =
            (sender, cert, chain, errors) => true;
        var inner = new SocketsHttpHandler();
        inner.SslOptions.RemoteCertificateValidationCallback = callerCallback;

        _ = new ApproovMessageHandler(inner);

        Assert.Same(callerCallback, inner.SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public void Constructor_CustomHttpClientHandler_KeepsCallerSuppliedCallback()
    {
        // USAGE.md documents wiring VerifyServerTrust by hand, so an existing callback is
        // left alone rather than overwritten.
        Func<HttpRequestMessage, System.Security.Cryptography.X509Certificates.X509Certificate2?,
            System.Security.Cryptography.X509Certificates.X509Chain?,
            System.Net.Security.SslPolicyErrors, bool> callerCallback =
            (request, cert, chain, errors) => true;
        var inner = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = callerCallback
        };

        _ = new ApproovMessageHandler(inner);

        Assert.Same(callerCallback, inner.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void Send_Synchronous_ThrowsRatherThanBypassingApproov()
    {
        // The transport deliberately supports synchronous Send. Without the override on
        // ApproovMessageHandler, DelegatingHandler.Send forwards straight to it and the
        // request reaches the network with no token and the placeholder secret intact.
        ApproovService.Initialize("test-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        var inner = new SyncCapableHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "PLACEHOLDER-SECRET");

        Assert.Throws<NotSupportedException>(() => client.Send(request));

        Assert.False(inner.Reached);
        Assert.Null(inner.LastRequest);
        Assert.Equal(0, ApproovService.FetchCallCount);
    }

    [Fact]
    public async Task SendAsync_Initialized_TokenHeaderAdded()
    {
        ApproovService.Initialize("dummy-config");
        HttpRequestMessage? captured = null;
        var inner = new FakeHandler(HttpStatusCode.OK, req => { captured = req; });
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        await client.GetAsync("https://example.com");
        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public async Task SendAsync_ShouldRetryDecision_ThrowsRetryableErrorWithoutSending()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<NetworkingErrorException>(
            () => client.GetAsync("https://example.com"));

        Assert.True(exception.ShouldRetry);
        Assert.False(inner.Reached);
    }

    [Fact]
    public async Task SendAsync_ShouldFailDecision_ThrowsApproovException()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Rejected, ARC = "ARC1", RejectionReasons = "r1" };
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        await Assert.ThrowsAsync<RejectionException>(() => client.GetAsync("https://example.com"));
    }

    [Fact]
    public async Task SendAsync_NotInitialized_ThrowsInitializationFailureException()
    {
        var inner = new FakeHandler(HttpStatusCode.OK);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InitializationFailureException>(
            () => client.GetAsync("https://example.com"));
    }

    [Fact]
    public void ApproovHttpClient_DefaultCtor_UsesApproovMessageHandler()
    {
        var client = new ApproovHttpClient();
        Assert.NotNull(client);
    }

    [Fact]
    public void CustomHttpClientHandler_DisablesAutomaticRedirects()
    {
        using var inner = new HttpClientHandler { AllowAutoRedirect = true };
        using var handler = new ApproovMessageHandler(inner);

        Assert.False(inner.AllowAutoRedirect);
    }

    [Fact]
    public void CustomHandlerWithoutRedirectControl_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new ApproovMessageHandler(new FakeHandler(HttpStatusCode.OK)));
    }

    [Fact]
    public async Task ApproovHttpClient_CustomHandlerCtor_SendsThroughHandler()
    {
        ApproovService.Initialize("");
        var inner = new FakeHandler(HttpStatusCode.Accepted);
        var handler = new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true);
        using var client = new ApproovHttpClient(handler);

        var response = await client.GetAsync("https://example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_Redirect_ReprocessesFinalUriWithoutStaleApproovHeaders()
    {
        ApproovService.Initialize("dummy-config");
        SetInstallSignatureResult();
        ApproovService.FetchResults.Enqueue(new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "first-token"
        });
        ApproovService.FetchResults.Enqueue(new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.UnknownUrl
        });
        var inner = new RedirectHandler(new Uri("https://example.com/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));

        var response = await client.GetAsync("https://example.com/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, ApproovService.FetchCallCount);
        Assert.Equal(new[]
        {
            "https://example.com/start",
            "https://example.com/final"
        }, ApproovService.FetchedUrls.ToArray());
        Assert.Equal(2, inner.Requests.Count);
        Assert.True(inner.Requests[0].Headers.Contains("Approov-Token"));
        Assert.True(inner.Requests[0].Headers.Contains("Signature"));
        Assert.False(inner.Requests[1].Headers.Contains("Approov-Token"));
        Assert.False(inner.Requests[1].Headers.Contains("Approov-TraceID"));
        Assert.False(inner.Requests[1].Headers.Contains("Signature"));
        Assert.False(inner.Requests[1].Headers.Contains("Signature-Input"));
    }

    [Fact]
    public async Task SendAsync_CrossOriginRedirect_StripsAuthorizationAndSubstitutionHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        ApproovService.FetchResults.Enqueue(new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "first-token"
        });
        ApproovService.FetchResults.Enqueue(new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.UnknownUrl
        });
        var inner = new RedirectHandler(new Uri("https://other.example/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/start");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
        request.Headers.Add("X-Api-Key", "secure-string-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.True(inner.Requests[0].Headers.Contains("Authorization"));
        Assert.True(inner.Requests[0].Headers.Contains("X-Api-Key"));
        Assert.False(inner.Requests[1].Headers.Contains("Authorization"));
        Assert.False(inner.Requests[1].Headers.Contains("X-Api-Key"));
        Assert.Equal(1, ApproovService.SecureStringCallCount);
    }

    [Fact]
    public async Task SendAsync_SameOriginRedirect_DoesNotSubstituteResolvedSecretAgain()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        var inner = new RedirectHandler(new Uri("https://example.com/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/start");
        request.Headers.Add("X-Api-Key", "secure-string-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, ApproovService.SecureStringCallCount);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("stub-secret",
            Assert.Single(inner.Requests[1].Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task SendAsync_Redirect_PreservesRequestOptions()
    {
        ApproovService.Initialize("");
        var inner = new RedirectHandler(new Uri("https://example.com/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/start");
        request.Options.Set(CorrelationOption, "correlation-value");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Requests[1].Options.TryGetValue(
            CorrelationOption, out string? correlation));
        Assert.Equal("correlation-value", correlation);
    }

    [Fact]
    public async Task SendAsync_CrossOriginRedirect_StripsSubstitutionQueryParameters()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionQueryParam("api_key");
        var inner = new RedirectHandler(new Uri(
            "https://other.example/final?api_key=reflected&keep=a%20b&encoded%2Dname=value"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));

        var response = await client.GetAsync(
            "https://example.com/start?api_key=first&api_key=second");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, ApproovService.SecureStringCallCount);
        Assert.Equal(2, inner.Requests.Count);
        string finalQuery = Uri.UnescapeDataString(
            inner.Requests[1].RequestUri!.Query);
        Assert.DoesNotContain("api_key", finalQuery,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("keep=a b", finalQuery);
        Assert.Contains("encoded-name=value", finalQuery);
    }

    [Fact]
    public async Task SendAsync_TemporaryRedirect_ReplaysInMemoryBodyExactly()
    {
        ApproovService.Initialize("");
        var inner = new BodyRedirectHandler(new Uri("https://example.com/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://example.com/start")
        {
            Content = new StringContent("redirect-body")
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "redirect-body", "redirect-body" }, inner.Bodies);
        Assert.Equal("redirect-body",
            await response.RequestMessage!.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendAsync_TemporaryRedirect_UnknownLengthBodyFailsClosed()
    {
        ApproovService.Initialize("");
        var inner = new BodyRedirectHandler(new Uri("https://example.com/final"));
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://example.com/start")
        {
            Content = new StreamContent(
                new NonSeekableStream(System.Text.Encoding.UTF8.GetBytes("one-shot")))
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendAsync(request));

        Assert.Contains("not replayable", exception.Message);
        Assert.Single(inner.Bodies);
    }

    [Fact]
    public async Task SendAsync_KnownLengthCustomContent_IsNotEagerlyBuffered()
    {
        ApproovService.Initialize("");
        var inner = new SerializationProbeHandler();
        using var client = new HttpClient(new ApproovMessageHandler(
            inner, automaticRedirectsAlreadyDisabled: true));
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://example.com/upload")
        {
            Content = new KnownLengthContent("stream-at-transport")
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(inner.WasSerializedBeforeTransport);
    }

    private static void SetInstallSignatureResult()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ApproovService.InstallSignatureResult = Convert.ToBase64String(
            ec.SignData(new byte[] { 4, 5, 6 }, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Action<HttpRequestMessage>? _capture;
        internal bool Reached { get; private set; }
        internal FakeHandler(HttpStatusCode status, Action<HttpRequestMessage>? capture = null)
        {
            _status = status; _capture = capture;
        }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Reached = true;
            _capture?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(_status)
                { RequestMessage = request });
        }
    }

    // Unlike FakeHandler, this overrides the synchronous Send overload, so a request that
    // slips past ApproovMessageHandler.Send would genuinely reach the transport instead of
    // throwing from the HttpMessageHandler base implementation.
    private sealed class SyncCapableHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        internal bool Reached { get; private set; }
        internal HttpRequestMessage? LastRequest { get; private set; }
        internal SyncCapableHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Record(request));

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken ct)
            => Record(request);

        private HttpResponseMessage Record(HttpRequestMessage request)
        {
            Reached = true;
            LastRequest = request;
            return new HttpResponseMessage(_status) { RequestMessage = request };
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        private readonly Uri _redirectUri;
        internal List<HttpRequestMessage> Requests { get; } = new();

        internal RedirectHandler(Uri redirectUri) => _redirectUri = redirectUri;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Requests.Count == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found)
                    { RequestMessage = request };
                redirect.Headers.Location = _redirectUri;
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request });
        }
    }

    private sealed class BodyRedirectHandler : HttpMessageHandler
    {
        private readonly Uri _redirectUri;
        internal List<string> Bodies { get; } = new();

        internal BodyRedirectHandler(Uri redirectUri) => _redirectUri = redirectUri;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));
            if (Bodies.Count == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                    { RequestMessage = request };
                redirect.Headers.Location = _redirectUri;
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request };
        }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        internal NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);
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
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }

    private sealed class SerializationProbeHandler : HttpMessageHandler
    {
        internal bool WasSerializedBeforeTransport { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = Assert.IsType<KnownLengthContent>(request.Content);
            WasSerializedBeforeTransport = content.SerializeCount != 0;
            await content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request };
        }
    }

    private sealed class KnownLengthContent : HttpContent
    {
        private readonly byte[] _body;
        internal int SerializeCount { get; private set; }

        internal KnownLengthContent(string body)
            => _body = System.Text.Encoding.UTF8.GetBytes(body);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context)
        {
            SerializeCount++;
            await stream.WriteAsync(_body);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }
}
