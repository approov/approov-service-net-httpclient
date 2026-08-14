// ApproovService.MAUI.Tests/util/ApproovDefaultMessageSigningTests.cs
// Tests for the retrofit-mirrored message signing: install (ES256) + account (HS256) modes.
using System;
using System.Formats.Asn1;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using Approov.Util.HttpSfv;
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

    // --- helpers ---------------------------------------------------------

    private static ApproovDefaultMessageSigning.SignatureParametersFactory MinimalFactory(bool account)
    {
        var baseParams = new SignatureParameters();
        baseParams.AddComponentIdentifier(new StringItem("@method"));
        var f = new ApproovDefaultMessageSigning.SignatureParametersFactory()
            .SetBaseParameters(baseParams)
            .SetAddApproovTokenHeader(true);
        return account ? f.SetUseAccountMessageSigning() : f.SetUseInstallMessageSigning();
    }

    private static (ApproovDefaultMessageSigning signer, HttpRequestMessage req, ApproovRequestMutations changes)
        Setup(ApproovDefaultMessageSigning.SignatureParametersFactory factory)
    {
        var signer = new ApproovDefaultMessageSigning().SetDefaultFactory(factory);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://shapes.approov.io/v5/shapes");
        req.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };
        return (signer, req, changes);
    }

    private static byte[] SignatureBytes(HttpRequestMessage req)
    {
        string v = string.Join("", req.Headers.GetValues("Signature"));      // label=:base64:
        int start = v.IndexOf(':') + 1;
        int end = v.LastIndexOf(':');
        return Convert.FromBase64String(v.Substring(start, end - start));
    }

    private static string SignatureInput(HttpRequestMessage req)
        => string.Join("", req.Headers.GetValues("Signature-Input"));

    // --- tests -----------------------------------------------------------

    [Fact]
    public void NoApproovToken_LeavesRequestUnsigned()
    {
        ApproovService.Initialize("dummy-config");
        var (signer, req, _) = Setup(MinimalFactory(account: true));
        var changesNoToken = new ApproovRequestMutations { TokenHeaderKey = null };

        var result = signer.HandleInterceptorProcessedRequest(req, changesNoToken);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void AccountMode_AddsAccountLabelledHmacSignature()
    {
        ApproovService.Initialize("dummy-config");
        byte[] hmac = new byte[32];
        for (int i = 0; i < hmac.Length; i++) hmac[i] = (byte)(i + 1);
        ApproovService.AccountSignatureResult = Convert.ToBase64String(hmac);

        var (signer, req, changes) = Setup(MinimalFactory(account: true));
        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.Equal(1, ApproovService.AccountSignatureCallCount);
        Assert.StartsWith("account=:", string.Join("", result.Headers.GetValues("Signature")));
        Assert.StartsWith("account=(", SignatureInput(result));
        Assert.Contains("alg=\"hmac-sha256\"", SignatureInput(result));
        Assert.Equal(hmac, SignatureBytes(result)); // used directly, no transform
    }

    [Fact]
    public void InstallMode_DecodesDerSignatureToRaw64Bytes()
    {
        ApproovService.Initialize("dummy-config");
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] der = ec.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        ApproovService.InstallSignatureResult = Convert.ToBase64String(der);

        var (signer, req, changes) = Setup(MinimalFactory(account: false));
        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.Equal(1, ApproovService.InstallSignatureCallCount);
        Assert.StartsWith("install=:", string.Join("", result.Headers.GetValues("Signature")));
        Assert.Contains("alg=\"ecdsa-p256-sha256\"", SignatureInput(result));
        Assert.Equal(64, SignatureBytes(result).Length); // raw R||S, RFC 9421 §3.3.4
    }

    [Fact]
    public void InstallMode_NoSignatureAvailable_SkipsSigning()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.InstallSignatureResult = null; // SDK cannot provide a signature

        var (signer, req, changes) = Setup(MinimalFactory(account: false));
        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void InstallMode_InvalidBase64_FailsOpenAndRemovesStaleSignatureHeaders()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.InstallSignatureResult = "not base64";
        var (signer, req, changes) = Setup(MinimalFactory(account: false));
        req.Headers.TryAddWithoutValidation("Signature", "stale=:AA==:");
        req.Headers.TryAddWithoutValidation("Signature-Input", "stale=();created=1");

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void InstallMode_InvalidDer_FailsOpen()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.InstallSignatureResult = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var (signer, req, changes) = Setup(MinimalFactory(account: false));

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void AccountMode_SdkException_FailsOpen()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AccountSignatureException = new InvalidOperationException("SDK unavailable");
        var (signer, req, changes) = Setup(MinimalFactory(account: true));

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.False(result.Headers.Contains("Signature"));
        Assert.False(result.Headers.Contains("Signature-Input"));
    }

    [Fact]
    public void DefaultFactory_CoversMethodTargetUriAndApproovTokenWithCreatedExpires()
    {
        ApproovService.Initialize("dummy-config");
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ApproovService.InstallSignatureResult = Convert.ToBase64String(
            ec.SignData(new byte[] { 9 }, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        var factory = ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory();
        factory.NowSeconds = () => 1000L; // deterministic created/expires
        var (signer, req, changes) = Setup(factory);

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.Equal(
            "install=(\"@method\" \"@target-uri\" \"approov-token\");alg=\"ecdsa-p256-sha256\";created=1000;expires=1015",
            SignatureInput(result));
    }

    [Fact]
    public void HostFactory_MatchesRequestWithExplicitPort()
    {
        // PutHostFactory is keyed by host name; a request to that host on a non-default
        // port must still select the host factory (RequestUri.Host, not Authority).
        ApproovService.Initialize("dummy-config");
        byte[] hmac = new byte[32];
        ApproovService.AccountSignatureResult = Convert.ToBase64String(hmac);

        var signer = new ApproovDefaultMessageSigning()
            .PutHostFactory("shapes.approov.io", MinimalFactory(account: true));
        var req = new HttpRequestMessage(HttpMethod.Get, "https://shapes.approov.io:8443/v5/shapes");
        req.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.True(result.Headers.Contains("Signature"));
        Assert.StartsWith("account=:", string.Join("", result.Headers.GetValues("Signature")));
    }

    [Fact]
    public void HostFactory_MatchesHostnameCaseInsensitively()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.InstallSignatureResult = null;
        ApproovService.AccountSignatureResult = Convert.ToBase64String(new byte[32]);
        var signer = new ApproovDefaultMessageSigning()
            .PutHostFactory("SHAPES.APPROOV.IO", MinimalFactory(account: true));
        var req = new HttpRequestMessage(HttpMethod.Get, "https://shapes.approov.io/v5/shapes");
        req.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };

        var result = signer.HandleInterceptorProcessedRequest(req, changes);

        Assert.True(result.Headers.Contains("Signature"));
        Assert.StartsWith("account=:", string.Join("", result.Headers.GetValues("Signature")));
    }

    [Fact]
    public void Sha512BodyDigest_IsGeneratedAndCoveredBySignature()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AccountSignatureResult = Convert.ToBase64String(new byte[32]);
        var factory = MinimalFactory(account: true)
            .SetBodyDigestConfig(ApproovDefaultMessageSigning.DIGEST_SHA512, required: true);
        var signer = new ApproovDefaultMessageSigning().SetDefaultFactory(factory);
        var body = "customer-production-payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent(body)
        };
        request.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };

        var result = signer.HandleInterceptorProcessedRequest(request, changes);

        string digest = string.Join("", result.Content!.Headers.GetValues("Content-Digest"));
        string expected = Convert.ToBase64String(
            SHA512.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
        Assert.Equal($"sha-512=:{expected}:", digest);
        Assert.Contains("\"content-digest\"", SignatureInput(result));
    }

    [Fact]
    public void RequiredBodyDigest_WithEmptyContent_IsGeneratedAndCoveredBySignature()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AccountSignatureResult = Convert.ToBase64String(new byte[32]);
        var factory = MinimalFactory(account: true)
            .SetBodyDigestConfig(ApproovDefaultMessageSigning.DIGEST_SHA256, required: true);
        var signer = new ApproovDefaultMessageSigning().SetDefaultFactory(factory);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("")
        };
        request.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };

        var result = signer.HandleInterceptorProcessedRequest(request, changes);

        string digest = string.Join("", result.Content!.Headers.GetValues("Content-Digest"));
        string expected = Convert.ToBase64String(SHA256.HashData(Array.Empty<byte>()));
        Assert.Equal($"sha-256=:{expected}:", digest);
        Assert.Contains("\"content-digest\"", SignatureInput(result));
    }

    [Fact]
    public void RequiredBodyDigest_WithUnknownLength_FailsClosed()
    {
        ApproovService.Initialize("dummy-config");
        var factory = MinimalFactory(account: true)
            .SetBodyDigestConfig(ApproovDefaultMessageSigning.DIGEST_SHA256, required: true);
        var signer = new ApproovDefaultMessageSigning().SetDefaultFactory(factory);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new UnknownLengthContent()
        };
        request.Headers.Add("Approov-Token", "the-token");
        var changes = new ApproovRequestMutations { TokenHeaderKey = "Approov-Token" };

        Assert.Throws<InvalidOperationException>(
            () => signer.HandleInterceptorProcessedRequest(request, changes));
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            System.IO.Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(new byte[] { 1, 2, 3 }).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public void DerEcdsaToRaw_RejectsTrailingDataAfterSequence()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(new BigInteger(0x1122));
            writer.WriteInteger(new BigInteger(0x33));
        }
        byte[] der = writer.Encode();
        byte[] withTrailing = new byte[der.Length + 1];
        Array.Copy(der, withTrailing, der.Length);
        withTrailing[der.Length] = 0x2A; // garbage appended after the outer SEQUENCE

        Assert.Throws<AsnContentException>(
            () => ApproovDefaultMessageSigning.DerEcdsaToRaw(withTrailing));
    }

    [Fact]
    public void DerEcdsaToRaw_ConvertsDerSequenceToFixed64Bytes()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(new BigInteger(0x1122)); // r
            writer.WriteInteger(new BigInteger(0x33));    // s
        }
        byte[] der = writer.Encode();

        byte[] raw = ApproovDefaultMessageSigning.DerEcdsaToRaw(der);

        Assert.Equal(64, raw.Length);
        Assert.Equal(0x11, raw[30]);   // r big-endian, right-aligned in first 32 bytes
        Assert.Equal(0x22, raw[31]);
        Assert.Equal(0x00, raw[29]);   // left-padded
        Assert.Equal(0x33, raw[63]);   // s big-endian, right-aligned in second 32 bytes
        Assert.Equal(0x00, raw[32]);
    }
}
