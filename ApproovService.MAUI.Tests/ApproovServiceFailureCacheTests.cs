// ApproovService.MAUI.Tests/ApproovServiceFailureCacheTests.cs
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceFailureCacheTests : IDisposable
{
    public ApproovServiceFailureCacheTests()
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
    public async Task FailureCache_CachesNetworkFailure_SecondCallSkipsPlatform()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetFailureCacheTTL(0.2);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };

        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        ApproovService.UpdateRequestWithApproov(req1);
        int callsAfterFirst = ApproovService.FetchCallCount;

        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        ApproovService.UpdateRequestWithApproov(req2);
        // Cached: no additional platform call
        Assert.Equal(callsAfterFirst, ApproovService.FetchCallCount);

        // After TTL, cache expires and platform is called again
        await Task.Delay(300);
        var req3 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        ApproovService.UpdateRequestWithApproov(req3);
        Assert.True(ApproovService.FetchCallCount > callsAfterFirst);
    }

    [Fact]
    public void FailureCache_TtlZero_DoesNotCacheNetworkFailure()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetFailureCacheTTL(0);
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };

        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response1 = ApproovService.UpdateRequestWithApproov(req1);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response1.Decision);
        Assert.Equal(1, ApproovService.FetchCallCount);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response2 = ApproovService.UpdateRequestWithApproov(req2);
        Assert.Equal(ApproovFetchDecision.ShouldProceed, response2.Decision);
        Assert.Equal(2, ApproovService.FetchCallCount);
    }

    [Fact]
    public async Task FailureCache_PlatformFetchThrows_DoesNotStrandNextCaller()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetFailureCacheTTL(5);

        // First caller: platform token fetch throws. The coalescing miss-group is
        // created before the throw, so it must be signalled/cleared even on failure.
        ApproovService.NextFetchException = new Exception("boom");
        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response1 = ApproovService.UpdateRequestWithApproov(req1);
        Assert.Equal(ApproovFetchDecision.ShouldFail, response1.Decision);

        // Second caller must not block forever on the (now stale) miss-group.
        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var second = Task.Run(() => ApproovService.UpdateRequestWithApproov(req2));
        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.True(completed == second, "second caller blocked forever after first fetch threw");
        Assert.Equal(ApproovFetchDecision.ShouldProceed, (await second).Decision);
    }

    [Fact]
    public void FailureCache_SecureStringNetworkFailure_ReusedByNextTokenFetch()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetFailureCacheTTL(5);
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };
        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req1.Headers.Add("X-Api-Key", "placeholder");

        var response1 = ApproovService.UpdateRequestWithApproov(req1);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response1.Decision);
        Assert.Equal(1, ApproovService.FetchCallCount);
        Assert.Equal(1, ApproovService.SecureStringCallCount);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response2 = ApproovService.UpdateRequestWithApproov(req2);
        Assert.Equal(ApproovFetchDecision.ShouldRetry, response2.Decision);
        Assert.Equal(1, ApproovService.FetchCallCount);
        Assert.Equal(1, ApproovService.SecureStringCallCount);
    }
}
