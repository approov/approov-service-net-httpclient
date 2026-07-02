// ApproovService.MAUI.Tests/ApproovServiceSecureStringTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceSecureStringTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.NextSecureStringResult = null;
        ApproovService.CustomJWTCallCount = 0;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void FetchSecureString_BypassMode_ReturnsUnknownKeyStatus()
    {
        ApproovService.Initialize("");
        var result = ApproovService.FetchSecureString("any-key");
        Assert.Equal(ApproovTokenFetchStatus.UnknownKey, result.Status);
    }

    [Fact]
    public void FetchSecureString_Initialized_ReturnsStubSecret()
    {
        ApproovService.Initialize("dummy-config");
        var result = ApproovService.FetchSecureString("valid-key");
        Assert.Equal(ApproovTokenFetchStatus.Success, result.Status);
        Assert.Equal("stub-secret", result.SecureString);
    }

    [Fact]
    public void SubstitutionHeader_ReplacesValueWithSecureString()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("X-Api-Key", "placeholder");
        var response = ApproovService.UpdateRequestWithApproov(req);
        string headerValue = string.Join("", response.Request!.Headers.GetValues("X-Api-Key"));
        Assert.Equal("stub-secret", headerValue);
    }

    [Fact]
    public void SubstitutionHeader_NullSecureString_PreservesOriginalPlaceholder()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        // Stub returns null SecureString → substitution code uses original value
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, SecureString = null };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("X-Api-Key", "placeholder-value");
        var response = ApproovService.UpdateRequestWithApproov(req);
        string headerValue = string.Join("", response.Request!.Headers.GetValues("X-Api-Key"));
        Assert.Equal("placeholder-value", headerValue);
    }

    [Fact]
    public void FetchCustomJWT_Initialized_ReturnsStubJwt()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.CustomJWTCallCount = 0;
        var result = ApproovService.FetchCustomJWT("{\"data\":\"test\"}");
        Assert.Equal(ApproovTokenFetchStatus.Success, result.Status);
        Assert.Equal("stub-jwt", result.Token);
        Assert.Equal(1, ApproovService.CustomJWTCallCount);
    }

    [Fact]
    public void FetchCustomJWT_NotInitialized_Throws()
    {
        Assert.Throws<InitializationFailureException>(
            () => ApproovService.FetchCustomJWT("{\"data\":\"test\"}"));
    }

    [Fact]
    public void FetchCustomJWT_BypassMode_DoesNotCallPlatformSdk()
    {
        ApproovService.Initialize("");
        ApproovService.CustomJWTCallCount = 0;
        // Same contract as the FetchApproovToken / FetchSecureString bypass guards:
        // no throw, no platform SDK call, empty-token result with a non-Success status
        var result = ApproovService.FetchCustomJWT("{\"data\":\"test\"}");
        Assert.Equal(0, ApproovService.CustomJWTCallCount);
        Assert.Equal(ApproovTokenFetchStatus.Disabled, result.Status);
        Assert.Equal("", result.Token);
    }
}
