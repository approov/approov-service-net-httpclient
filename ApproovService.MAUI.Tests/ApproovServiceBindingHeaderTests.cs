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
    public void SetBindingHeader_MissingHeader_DoesNotChangeHashAndFetchesToken()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        // No Authorization header on request
        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(0, ApproovService.SetDataHashCallCount);
        Assert.Equal(1, ApproovService.FetchCallCount);
    }

    [Fact]
    public void SetBindingHeader_MissingAfterBoundRequest_RetainsSdkHash()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("Authorization");
        var bound = new HttpRequestMessage(HttpMethod.Get, "https://example.com/bound");
        bound.Headers.Add("Authorization", "Bearer first");
        ApproovService.UpdateRequestWithApproov(bound);

        var missing = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/missing");
        var response = ApproovService.UpdateRequestWithApproov(missing);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("Bearer first", ApproovService.LastDataHashValue);
        Assert.Equal(2, ApproovService.FetchCallCount);
    }

    [Fact]
    public void SetBindingHeader_MultipleHeaderValues_ForwardsSerializedValues()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.TryAddWithoutValidation("X-Bind", new[] { "first", "second" });

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("first,second", ApproovService.LastDataHashValue);
        Assert.Equal(1, ApproovService.FetchCallCount);
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

    [Fact]
    public async Task SetBindingHeader_UnboundFetchCannotRaceBoundHashUpdate()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        ApproovService.BlockTokenFetch = true;
        ApproovService.ReleaseTokenFetch.Reset();

        var unboundRequest = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/unbound");
        var unbound = Task.Run(
            () => ApproovService.UpdateRequestWithApproov(unboundRequest));
        Assert.True(ApproovService.TokenFetchStarted.Wait(TimeSpan.FromSeconds(5)));

        var boundRequest = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/bound");
        boundRequest.Headers.Add("X-Bind", "bound");
        var bound = Task.Run(
            () => ApproovService.UpdateRequestWithApproov(boundRequest));

        await Task.Delay(100);
        Assert.Equal(0, ApproovService.SetDataHashCallCount);
        Assert.Equal(1, ApproovService.FetchCallCount);

        ApproovService.ReleaseTokenFetch.Set();
        var responses = await Task.WhenAll(unbound, bound);

        Assert.All(responses,
            response => Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision));
        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("bound", ApproovService.LastDataHashValue);
        Assert.Equal(2, ApproovService.FetchCallCount);
    }

    [Fact]
    public async Task SetDataHashInToken_CannotInterruptBoundRequestFetch()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        ApproovService.BlockTokenFetch = true;
        ApproovService.ReleaseTokenFetch.Reset();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/bound");
        request.Headers.Add("X-Bind", "request-binding");
        var boundRequest = Task.Run(
            () => ApproovService.UpdateRequestWithApproov(request));
        Assert.True(ApproovService.TokenFetchStarted.Wait(TimeSpan.FromSeconds(5)));

        var directSet = Task.Run(
            () => ApproovService.SetDataHashInToken("direct-binding"));
        await Task.Delay(100);

        Assert.Equal(1, ApproovService.SetDataHashCallCount);
        Assert.Equal("request-binding", ApproovService.LastDataHashValue);

        ApproovService.ReleaseTokenFetch.Set();
        var response = await boundRequest;
        await directSet;

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(2, ApproovService.SetDataHashCallCount);
        Assert.Equal("direct-binding", ApproovService.LastDataHashValue);
    }

    [Fact]
    public async Task FetchApproovToken_CannotEnterDuringBoundRequestFetch()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetBindingHeader("X-Bind");
        ApproovService.BlockTokenFetch = true;
        ApproovService.ReleaseTokenFetch.Reset();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "https://example.com/bound");
        request.Headers.Add("X-Bind", "request-binding");
        var boundRequest = Task.Run(
            () => ApproovService.UpdateRequestWithApproov(request));
        Assert.True(ApproovService.TokenFetchStarted.Wait(TimeSpan.FromSeconds(5)));

        var directFetch = Task.Run(
            () => ApproovService.FetchApproovToken("https://example.com/direct"));
        await Task.Delay(100);
        Assert.Equal(1, ApproovService.FetchCallCount);

        ApproovService.ReleaseTokenFetch.Set();
        var response = await boundRequest;
        var directResult = await directFetch;

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(ApproovTokenFetchStatus.Success, directResult.Status);
        Assert.Equal(2, ApproovService.FetchCallCount);
    }
}
