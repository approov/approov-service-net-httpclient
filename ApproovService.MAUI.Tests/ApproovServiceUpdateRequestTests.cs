// ApproovService.MAUI.Tests/ApproovServiceUpdateRequestTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceUpdateRequestTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.FetchCallCount = 0;
        ApproovService.NextFetchResult = null;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void UpdateRequest_NotInitialized_ReturnsFailDecision()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldFail, response.Decision);
    }

    [Fact]
    public void UpdateRequest_BypassMode_ReturnsIgnoreDecision()
    {
        ApproovService.Initialize("");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldIgnore, response.Decision);
    }

    [Fact]
    public void UpdateRequest_Initialized_TokenAddedToHeader()
    {
        ApproovService.Initialize("dummy-config");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.True(response.Request!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public void UpdateRequest_ExcludedURL_ReturnsIgnoreDecision()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddExclusionURLRegex("internal", @"https://internal\.example\.com/.*");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://internal.example.com/api");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldIgnore, response.Decision);
    }

    [Fact]
    public void UpdateRequest_NoNetworkResult_ReturnsRetryDecision()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response.Decision);
    }
}
