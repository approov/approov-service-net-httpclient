using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceTlsPinningTests : IDisposable
{
    public ApproovServiceTlsPinningTests()
    {
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    public void Dispose()
    {
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    // ----- VerifyPinning (pinning across the validated chain) -----

    [Fact]
    public void VerifyPinning_BypassMode_AlwaysReturnsTrue()
    {
        ApproovService.Initialize("");
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_NotInitialized_AlwaysReturnsTrue()
    {
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_Initialized_NoPinsJson_ReturnsTrue()
    {
        // PlatformGetPinsJSON stub returns null — all pins pass
        ApproovService.Initialize("dummy-config");
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
        Assert.Equal("public-key-sha256", ApproovService.LastPinType);
    }

    [Fact]
    public void VerifyPinning_PinsJsonMissingHost_ReturnsTrue()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.PinsJson = "{\"other.example.com\":[\"pin\"]}";
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_MatchingPin_ReturnsTrue()
    {
        ApproovService.Initialize("dummy-config");
        byte[] publicKeyBytes = { 1, 2, 3, 4 };
        ApproovService.PublicKeyBytes = publicKeyBytes;
        ApproovService.PinsJson = $"{{\"example.com\":[\"{PinFor(publicKeyBytes)}\"]}}";
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_MismatchedPin_ReturnsFalse()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.PublicKeyBytes = new byte[] { 1, 2, 3, 4 };
        ApproovService.PinsJson = "{\"example.com\":[\"not-the-pin\"]}";
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.False(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_ExtractionReturnsNullForAllElements_ReturnsFalse()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.ExtractReturnsNull = true;
        ApproovService.PinsJson = "{\"example.com\":[\"configured-pin\"]}";
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.False(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_MutatorSkipsRequest_ReturnsTrueWithoutFetchingPins()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new SkipPinningMutator());
        ApproovService.PinsJson = "{\"example.com\":[\"configured-pin\"]}";
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
        Assert.Null(ApproovService.LastPinType);
    }

    [Fact]
    public void VerifyPinning_MatchesIntermediateInChain_ReturnsTrue()
    {
        // Only the issuer (CA) public key is pinned, not the leaf. The pin must be
        // matched by walking the whole chain, not just the leaf certificate.
        ApproovService.Initialize("dummy-config");
        using var ca = CreateCaCert();
        using var leaf = CreateCertSignedBy(ca);
        ApproovService.PinsJson = $"{{\"example.com\":[\"{PinForCert(ca)}\"]}}";
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { leaf, ca }));
    }

    [Fact]
    public void VerifyPinning_EmptyHostPinList_FallsBackToManagedRoots()
    {
        // An empty pin list for the host means "use the managed trust roots" pinned
        // under the "*" entry.
        ApproovService.Initialize("dummy-config");
        var cert = CreateSelfSignedCert();
        ApproovService.PinsJson = $"{{\"example.com\":[],\"*\":[\"{PinForCert(cert)}\"]}}";
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    [Fact]
    public void VerifyPinning_EmptyHostPinList_NoManagedRoots_ReturnsTrue()
    {
        // Host present but no pins and no "*" managed roots: not actively pinned, so a
        // chain that already passed certificate validation is accepted.
        ApproovService.Initialize("dummy-config");
        var cert = CreateSelfSignedCert();
        ApproovService.PinsJson = "{\"example.com\":[]}";
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyPinning(req, new[] { cert }));
    }

    // ----- VerifyServerTrust (certificate validation + pinning) -----

    [Fact]
    public void VerifyServerTrust_NonNonePolicyErrors_ReturnsFalse()
    {
        // Even when pinning would pass (here: uninitialized => VerifyPinning returns true),
        // a certificate that failed normal TLS validation must be rejected.
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.False(ApproovService.VerifyServerTrust(
            req, cert, ChainFor(cert), SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(ApproovService.VerifyServerTrust(
            req, cert, ChainFor(cert), SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void VerifyServerTrust_NoneErrors_DelegatesToPinning()
    {
        // Valid certificate (no policy errors) and matching pin => trusted.
        ApproovService.Initialize("dummy-config");
        var cert = CreateSelfSignedCert();
        ApproovService.PinsJson = $"{{\"example.com\":[\"{PinForCert(cert)}\"]}}";
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.True(ApproovService.VerifyServerTrust(
            req, cert, ChainFor(cert), SslPolicyErrors.None));
    }

    [Fact]
    public void VerifyServerTrust_NullCertOrChain_ReturnsFalse()
    {
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        Assert.False(ApproovService.VerifyServerTrust(req, null, ChainFor(cert), SslPolicyErrors.None));
        Assert.False(ApproovService.VerifyServerTrust(req, cert, null, SslPolicyErrors.None));
    }

    // ----- helpers -----

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));
    }

    private static X509Certificate2 CreateCaCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Test CA", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));
    }

    private static X509Certificate2 CreateCertSignedBy(X509Certificate2 issuer)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=leaf", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return req.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365), new byte[] { 1, 2, 3, 4 });
    }

    // Build an X509Chain whose ChainElements are the supplied certificates (leaf first).
    // Certificate validity is intentionally not enforced here — VerifyServerTrust owns the
    // trust decision; VerifyPinning only inspects the chain's public keys.
    private static X509Chain ChainFor(params X509Certificate2[] certs)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        for (int i = 1; i < certs.Length; i++)
            chain.ChainPolicy.ExtraStore.Add(certs[i]);
        chain.Build(certs[0]);
        return chain;
    }

    private static string PinFor(byte[] publicKeyBytes)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(publicKeyBytes));
    }

    private static string PinForCert(X509Certificate2 cert)
        => PinFor(cert.PublicKey.ExportSubjectPublicKeyInfo());

    private sealed class SkipPinningMutator : IApproovServiceMutator
    {
        public void HandlePrecheckResult(IApproovTokenFetchResult result) { }
        public void HandleFetchTokenResult(IApproovTokenFetchResult result) { }
        public void HandleFetchSecureStringResult(
            IApproovTokenFetchResult result, string operation, string key) { }
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult result) { }
        public bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request) => true;
        public bool HandleInterceptorFetchTokenResult(
            IApproovTokenFetchResult result, string url) => true;
        public bool HandleInterceptorHeaderSubstitutionResult(
            IApproovTokenFetchResult result, string header) => true;
        public bool HandleInterceptorQueryParamSubstitutionResult(
            IApproovTokenFetchResult result, string queryKey) => true;
        public HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage request, ApproovRequestMutations changes) => request;
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) => false;
    }
}
