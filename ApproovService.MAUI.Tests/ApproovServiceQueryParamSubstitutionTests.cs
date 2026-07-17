using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceQueryParamSubstitutionTests : IDisposable
{
    public ApproovServiceQueryParamSubstitutionTests()
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
    public void UpdateRequest_QueryParamSubstitution_ReplacesValue()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionQueryParam("api_key");
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?api_key=placeholder&other=value");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        string query = response.Request!.RequestUri!.Query;
        // Stub PlatformFetchSecureStringAndWait returns SecureString="stub-secret"
        Assert.Contains("api_key=stub-secret", Uri.UnescapeDataString(query));
        Assert.Contains("other=value", query);
    }

    [Fact]
    public void UpdateRequest_NonSubstitutableQueryParam_LeftUnchanged()
    {
        ApproovService.Initialize("dummy-config");
        // No substitution params configured
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?foo=bar");
        var response = ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Contains("foo=bar", response.Request!.RequestUri!.Query);
    }

    [Fact]
    public void UpdateRequest_QueryParamSubstitutionUnknownKey_LeavesValueUnchanged()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.UnknownKey };
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?api_key=placeholder");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Contains("api_key=placeholder", response.Request!.RequestUri!.Query);
    }

    [Fact]
    public void UpdateRequest_QueryParamSubstitutionNetworkFailure_ProceedsUnchanged()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, SecureString = "must-not-use" };
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?api_key=placeholder");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Contains("api_key=placeholder", response.Request!.RequestUri!.Query);
    }

    [Fact]
    public void UpdateRequest_CustomMutatorCannotSubstituteNonSuccessQueryResult()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new AllowAllSubstitutionsMutator());
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.UnknownKey, SecureString = "must-not-use" };
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?api_key=placeholder");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Contains("api_key=placeholder", response.Request!.RequestUri!.Query);
    }

    private sealed class AllowAllSubstitutionsMutator : IApproovServiceMutator
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
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) => true;
    }
}
