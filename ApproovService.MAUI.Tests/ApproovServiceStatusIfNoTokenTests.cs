// ApproovService.MAUI.Tests/ApproovServiceStatusIfNoTokenTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceStatusIfNoTokenTests : IDisposable
{
    public ApproovServiceStatusIfNoTokenTests()
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
    public void UseApproovStatusIfNoToken_Disabled_ReturnsRetry()
    {
        ApproovService.Initialize("dummy-config");
        // Flag off by default
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response.Decision);
    }

    [Fact]
    public void UseApproovStatusIfNoToken_Enabled_InjectsStatusString()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.True(response.Request!.Headers.Contains("Approov-Token"));
        string headerValue = string.Join("", response.Request!.Headers.GetValues("Approov-Token"));
        Assert.Equal("NoNetwork", headerValue);
    }

    [Fact]
    public void UseApproovStatusIfNoToken_Enabled_AppliesConfiguredPrefix()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.SetApproovTokenHeader("Approov-Token", "Status ");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        string headerValue = string.Join("", response.Request!.Headers.GetValues("Approov-Token"));
        Assert.Equal("Status NoNetwork", headerValue);
    }

    [Fact]
    public void UseApproovStatusIfNoToken_Enabled_RealTokenStillUsedNormally()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        // Default stub returns Success + "stub-token"
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        string headerValue = string.Join("", response.Request!.Headers.GetValues("Approov-Token"));
        Assert.Equal("stub-token", headerValue);
    }
}
