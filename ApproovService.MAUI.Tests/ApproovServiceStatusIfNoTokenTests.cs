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
    public void UseApproovStatusIfNoToken_Enabled_DefaultNetworkFailureStillReturnsRetry()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response.Decision);
        Assert.False(response.Request!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public void UseApproovStatusIfNoToken_CustomMutatorAllowsNetworkFailure_InjectsUppercaseStatus()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.SetApproovTokenHeader("Approov-Token", "Status ");
        ApproovService.SetServiceMutator(new AllowStatusMutator());
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        string headerValue = string.Join("", response.Request!.Headers.GetValues("Approov-Token"));
        Assert.Equal("Status NO_NETWORK", headerValue);
    }

    [Fact]
    public void UseApproovStatusIfNoToken_NoApproovService_InjectsUppercaseStatusByDefault()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoApproovService, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal("NO_APPROOV_SERVICE",
            string.Join("", response.Request!.Headers.GetValues("Approov-Token")));
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

    private sealed class AllowStatusMutator : IApproovServiceMutator
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
            IApproovTokenFetchResult result, string header) => false;
        public bool HandleInterceptorQueryParamSubstitutionResult(
            IApproovTokenFetchResult result, string queryKey) => false;
        public HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage request, ApproovRequestMutations changes) => request;
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) => true;
    }
}
