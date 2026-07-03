// ApproovService.MAUI.Tests/util/ApproovDefaultMessageSigningTests.cs
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovDefaultMessageSigningTests : IDisposable
{
    public ApproovDefaultMessageSigningTests()
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
    public void SignRequest_DefaultMutator_ReturnsUnchangedRequest()
    {
        // Default mutator's GetSignatureParametersFactory returns null (no signing)
        ApproovService.Initialize("dummy-config");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var tokenResult = new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, Token = "t" };
        var result = ApproovService.SignRequest(req, tokenResult);
        // No Signature header expected when factory returns null
        Assert.False(result.Headers.Contains("Signature"));
    }

    [Fact]
    public void SignRequest_WithSigningKey_AddsSignatureAndInputHeaders()
    {
        ApproovService.Initialize("dummy-config");
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        byte[] key = ecdsa.ExportPkcs8PrivateKey();
        ApproovService.SetServiceMutator(new SigningMutator(key));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        var tokenResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, Token = "t" };
        var result = ApproovService.SignRequest(req, tokenResult);

        Assert.True(result.Headers.Contains("Signature"));
        Assert.True(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void SignRequest_NullSigningKey_FailOpenNoSignatureHeaders()
    {
        ApproovService.Initialize("dummy-config");
        // GetSigningKey() returns null → fail-open, no headers added
        ApproovService.SetServiceMutator(new SigningMutator(null));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        var tokenResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, Token = "t" };
        var result = ApproovService.SignRequest(req, tokenResult);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void SignRequest_RepeatSigning_DERNeverCrashes()
    {
        // Generates 60 signatures across different messages.
        // EC signatures produce random r/s values; this exercises DER edge cases
        // (leading-zero stripping, high-bit padding) without requiring them to appear.
        ApproovService.Initialize("dummy-config");
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        byte[] key = ecdsa.ExportPkcs8PrivateKey();
        ApproovService.SetServiceMutator(new SigningMutator(key));

        for (int i = 0; i < 60; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://example.com/api/{i}");
            var tokenResult = new StubTokenFetchResult
                { Status = ApproovTokenFetchStatus.Success, Token = $"token-{i}" };
            var result = ApproovService.SignRequest(req, tokenResult);
            Assert.True(result.Headers.Contains("Signature"),
                $"Iteration {i}: Signature header missing");
        }
    }

    [Fact]
    public async Task Pipeline_NullSigningKey_RequestProceedsUnsignedWithoutError()
    {
        // The ONLY fail-open case: the platform cannot provide a signature
        // (no signing key). The request proceeds without Signature headers.
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new SigningMutator(null));
        var inner = new RecordingHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(inner.Reached);
        Assert.False(req.Headers.Contains("Signature"));
        Assert.False(req.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void SignRequest_InvalidKeyDerDecodeError_Propagates()
    {
        // A malformed PKCS#8 key produces an ASN.1/DER decode error inside the
        // signer: a legitimate error that must NOT be swallowed (fail closed)
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new SigningMutator(new byte[] { 0x01, 0x02, 0x03 }));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        var tokenResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, Token = "t" };

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => ApproovService.SignRequest(req, tokenResult));
    }

    [Fact]
    public async Task Pipeline_SignerDerDecodeError_FailsRequestClosed()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new SigningMutator(new byte[] { 0x01, 0x02, 0x03 }));
        var inner = new RecordingHandler();
        var handler = new ApproovMessageHandler(inner);
        using var client = new HttpClient(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(
            () => client.SendAsync(req));
        Assert.False(inner.Reached);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool Reached { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Reached = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class SigningMutator : IApproovServiceMutator, Approov.Util.Sig.IApproovMessageSigner
    {
        private readonly byte[]? _key;
        public SigningMutator(byte[]? key) => _key = key;

        public void HandlePrecheckResult(IApproovTokenFetchResult r) { }
        public void HandleFetchTokenResult(IApproovTokenFetchResult r) { }
        public void HandleFetchSecureStringResult(IApproovTokenFetchResult r, string op, string key) { }
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult r) { }
        public bool HandleInterceptorShouldProcessRequest(System.Net.Http.HttpRequestMessage req) => true;
        public bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult r, string url) => true;
        public bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult r, string h) => true;
        public bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult r, string k) => true;
        public System.Net.Http.HttpRequestMessage HandleInterceptorProcessedRequest(
            System.Net.Http.HttpRequestMessage req, ApproovRequestMutations c) => req;
        public bool HandlePinningShouldProcessRequest(System.Net.Http.HttpRequestMessage req) => true;

        Approov.Util.Sig.SignatureParametersFactory? Approov.Util.Sig.IApproovMessageSigner.GetSignatureParametersFactory()
        {
            return (req, token) =>
            {
                var sp = new Approov.Util.Sig.SignatureParameters();
                sp.AddComponentIdentifier(new Approov.Util.HttpSfv.StringItem("@method"));
                sp.AddParameter("created", 1735000000L);
                return sp;
            };
        }

        byte[]? Approov.Util.Sig.IApproovMessageSigner.GetSigningKey() => _key;
        string Approov.Util.Sig.IApproovMessageSigner.SignatureLabel => "sig1";
        string Approov.Util.Sig.IApproovMessageSigner.SignatureParamsLabel => "sig-params";
    }
}
