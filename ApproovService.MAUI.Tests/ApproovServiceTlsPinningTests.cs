using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceTlsPinningTests : IDisposable
{
    public void Dispose() => ApproovService.ResetForTesting();

    [Fact]
    public void VerifyPinning_BypassMode_AlwaysReturnsTrue()
    {
        ApproovService.Initialize("");
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, cert));
    }

    [Fact]
    public void VerifyPinning_NotInitialized_AlwaysReturnsTrue()
    {
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, cert));
    }

    [Fact]
    public void VerifyPinning_Initialized_NoPinsJson_ReturnsTrue()
    {
        // PlatformGetPinsJSON stub returns null — all pins pass
        ApproovService.Initialize("dummy-config");
        var cert = CreateSelfSignedCert();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(ApproovService.VerifyPinning(req, cert));
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));
    }
}
