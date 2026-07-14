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
}
