// ApproovService.MAUI.Tests/ApproovServiceBindingHeaderTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceBindingHeaderTests : IDisposable
{
    public ApproovServiceBindingHeaderTests()
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
    public void SetBindingHeader_PresentHeader_ForwardsValueToSdk()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("Authorization", "Bearer my-token");
        ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("Bearer my-token", ApproovService.LastDataHashValue);
    }

    [Fact]
    public void SetBindingHeader_MissingHeader_DoesNotCallSetDataHash()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        // No Authorization header on request
        ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(0, ApproovService.SetDataHashCallCount);
    }

    [Fact]
    public void SetBindingHeader_EmptyHeaderValue_ForwardsEmptyString()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.TryAddWithoutValidation("X-Bind", "");
        ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("", ApproovService.LastDataHashValue);
    }

    [Fact]
    public void SetDataHashInToken_DirectCall_RecordedByStub()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetDataHashInToken("some-data");
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("some-data", ApproovService.LastDataHashValue);
    }
}
