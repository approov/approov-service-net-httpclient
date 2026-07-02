// ApproovService.MAUI.Tests/ApproovServiceFailureCacheTests.cs
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceFailureCacheTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.FetchCallCount = 0;
        ApproovService.NextFetchResult = null;
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
}
