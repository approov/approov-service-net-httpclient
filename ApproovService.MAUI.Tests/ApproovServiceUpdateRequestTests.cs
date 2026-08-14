// ApproovService.MAUI.Tests/ApproovServiceUpdateRequestTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceUpdateRequestTests : IDisposable
{
    public ApproovServiceUpdateRequestTests()
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

    [Fact]
    public void UpdateRequest_AfterSetServiceMutatorNull_UsesDefaultFailClosedBehavior()
    {
        ApproovService.Initialize("dummy-config");

        // Custom fail-open mutator: a Rejected token result proceeds without a token
        ApproovService.SetServiceMutator(new FailOpenMutator());
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Rejected, Token = "", ARC = "ARC1", RejectionReasons = "r1" };
        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response1 = ApproovService.UpdateRequestWithApproov(req1);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response1.Decision);

        // Null restores the default mutator: the same Rejected result must now fail closed
        ApproovService.SetServiceMutator(null);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Rejected, Token = "", ARC = "ARC1", RejectionReasons = "r1" };
        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response2 = ApproovService.UpdateRequestWithApproov(req2);
        Assert.Equal(ApproovFetchDecision.ShouldFail, response2.Decision);
        Assert.IsType<RejectionException>(response2.Error);
    }

    private sealed class FailOpenMutator : IApproovServiceMutator
    {
        public void HandlePrecheckResult(IApproovTokenFetchResult r) { }
        public void HandleFetchTokenResult(IApproovTokenFetchResult r) { }
        public void HandleFetchSecureStringResult(IApproovTokenFetchResult r, string op, string key) { }
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult r) { }
        public bool HandleInterceptorShouldProcessRequest(HttpRequestMessage req) => true;
        public bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult r, string url) =>
            r.Status == ApproovTokenFetchStatus.Success;
        public bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult r, string h) => false;
        public bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult r, string k) => false;
        public HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage req, ApproovRequestMutations ch) => req;
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage req) => true;
    }
}
