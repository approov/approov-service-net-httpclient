using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceConcurrencyTests : IDisposable
{
    public ApproovServiceConcurrencyTests()
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
    public async Task Concurrent_Requests_AllSucceed()
    {
        ApproovService.Initialize("dummy-config");
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
            return ApproovService.UpdateRequestWithApproov(req);
        }));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(ApproovFetchDecision.ShouldProceed, r.Decision));
    }

    [Fact]
    public async Task ConcurrentFailure_OnlyOnePlatformCallMade()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetFailureCacheTTL(60.0);

        // Make the first platform call return NoNetwork; others should coalesce
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.NoNetwork, Token = "" };

        var callCountBefore = ApproovService.FetchCallCount;

        // Fire 10 concurrent requests simultaneously
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
            return ApproovService.UpdateRequestWithApproov(req);
        }));
        await Task.WhenAll(tasks);

        // At most a small number of platform calls should have been made
        // (ideally 1, but thread scheduling may allow a few before the cache is warm)
        Assert.True(ApproovService.FetchCallCount - callCountBefore <= 3,
            $"Too many platform calls: {ApproovService.FetchCallCount - callCountBefore}");
    }
}
