// ApproovService.MAUI.Tests/ApproovServiceBindingHeaderTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceBindingHeaderTests : IDisposable
{
    public ApproovServiceBindingHeaderTests()
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
    public void SetBindingHeader_PresentHeader_ForwardsValueToSdk()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("Authorization", "Bearer my-token");
        ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("Bearer my-token", ApproovService.LastDataHashValue);
    }

    [Fact]
    public void SetBindingHeader_MissingHeader_FailsWithoutFetchingToken()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        // No Authorization header on request
        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldFail, response.Decision);
        Assert.IsType<ConfigurationFailureException>(response.Error);
        Assert.Equal(0, ApproovService.SetDataHashCallCount);
        Assert.Equal(0, ApproovService.FetchCallCount);
    }

    [Fact]
    public void SetBindingHeader_MultipleHeaderValues_FailsWithoutFetchingToken()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.TryAddWithoutValidation("X-Bind", new[] { "first", "second" });

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldFail, response.Decision);
        Assert.IsType<ConfigurationFailureException>(response.Error);
        Assert.Equal(0, ApproovService.SetDataHashCallCount);
        Assert.Equal(0, ApproovService.FetchCallCount);
    }

    [Fact]
    public void SetBindingHeader_EmptyHeaderValue_ForwardsEmptyString()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.TryAddWithoutValidation("X-Bind", "");
        ApproovService.UpdateRequestWithApproov(req);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("", ApproovService.LastDataHashValue);
    }

    [Fact]
    public void SetDataHashInToken_DirectCall_RecordedByStub()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetDataHashInToken("some-data");
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("some-data", ApproovService.LastDataHashValue);
    }

    [Fact]
    public async Task SetBindingHeader_ConcurrentRequests_KeepBindingAndFetchAtomic()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        ApproovService.BlockTokenFetch = true;
        ApproovService.ReleaseTokenFetch.Reset();

        var firstRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/first");
        firstRequest.Headers.Add("X-Bind", "first");
        var first = Task.Run(() => ApproovService.UpdateRequestWithApproov(firstRequest));
        Assert.True(ApproovService.TokenFetchStarted.Wait(TimeSpan.FromSeconds(5)));

        var secondRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/second");
        secondRequest.Headers.Add("X-Bind", "second");
        var second = Task.Run(() => ApproovService.UpdateRequestWithApproov(secondRequest));

        await Task.Delay(100);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("first", ApproovService.LastDataHashValue);

        ApproovService.ReleaseTokenFetch.Set();
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses,
            response => Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision));
        Assert.Equal(2, ApproovService.SetDataHashCallCount);
        Assert.Equal(2, ApproovService.FetchCallCount);
    }
}
