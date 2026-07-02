// ApproovService.MAUI.Tests/ApproovServiceInitTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceInitTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.InitCallCount = 0;
        ApproovService.NextInitShouldThrow = false;
        ApproovService.NextInitReturnsFalse = false;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void Initialize_EmptyConfig_SetsBypassMode()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
    }

    [Fact]
    public void Initialize_SameConfigTwice_DoesNotThrow()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.Initialize("dummy-config");
    }

    [Fact]
    public void Initialize_DifferentConfigTwice_ForwardsAndSurfacesPlatformRejection()
    {
        ApproovService.Initialize("config-a");
        ApproovService.NextInitShouldThrow = true;
        Assert.Throws<Exception>(() =>
            ApproovService.Initialize("config-b"));
        Assert.True(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void SetApproovTokenHeader_RoundTrip()
    {
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        var (header, prefix) = ApproovService.GetApproovTokenHeader();
        Assert.Equal("X-Custom", header);
        Assert.Equal("Bearer ", prefix);
    }

    [Fact]
    public void SetApproovTraceIDHeader_RoundTrip()
    {
        ApproovService.SetApproovTraceIDHeader("Approov-TraceID");
        Assert.Equal("Approov-TraceID", ApproovService.GetApproovTraceIDHeader());
    }

    [Fact]
    public void ResetForTesting_ClearsInitialization()
    {
        ApproovService.Initialize("some-config");
        ApproovService.ResetForTesting();
        Assert.False(ApproovService.IsInitialized());
    }

    [Fact]
    public void SetServiceMutator_RoundTrip()
    {
        var custom = new NoOpMutator();
        ApproovService.SetServiceMutator(custom);
        Assert.Same(custom, ApproovService.GetServiceMutator());
    }

    private sealed class NoOpMutator : IApproovServiceMutator
    {
        public void HandlePrecheckResult(IApproovTokenFetchResult r) { }
        public void HandleFetchTokenResult(IApproovTokenFetchResult r) { }
        public void HandleFetchSecureStringResult(IApproovTokenFetchResult r, string op, string key) { }
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult r) { }
        public bool HandleInterceptorShouldProcessRequest(System.Net.Http.HttpRequestMessage req) => true;
        public bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult r, string url) => true;
        public bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult r, string h) => true;
        public bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult r, string k) => true;
        public System.Net.Http.HttpRequestMessage HandleInterceptorProcessedRequest(
            System.Net.Http.HttpRequestMessage req, ApproovRequestMutations ch) => req;
        public bool HandlePinningShouldProcessRequest(System.Net.Http.HttpRequestMessage req) => true;
    }
}
