// ApproovService.MAUI.Tests/ApproovServiceInitializationEdgeCaseTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceInitializationEdgeCaseTests : IDisposable
{
    public ApproovServiceInitializationEdgeCaseTests()
    {
        // Reset shared stub state in case a prior test class left it dirty
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    public void Dispose()
    {
        ApproovService.ResetPlatformStub();
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void Initialize_EmptyThenValid_UpgradesSuccessfully()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());

        ApproovService.Initialize("real-config");
        Assert.True(ApproovService.IsInitialized());
        Assert.True(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void Initialize_EmptyAfterValid_IsIgnored()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        ApproovService.AddSubstitutionHeader("Authorization", null);
        ApproovService.AddSubstitutionQueryParam("apikey");
        ApproovService.AddExclusionURLRegex("health", ".*health.*");
        int callsBefore = ApproovService.InitCallCount;

        ApproovService.Initialize("");

        Assert.Equal(callsBefore, ApproovService.InitCallCount);
        Assert.True(ApproovService.IsInitialized());
        Assert.True(ApproovService.IsApproovEnabled());
        var (header, prefix) = ApproovService.GetApproovTokenHeader();
        Assert.Equal("X-Custom", header);
        Assert.Equal("Bearer ", prefix);
        Assert.True(ApproovService.GetSubstitutionHeaders().ContainsKey("Authorization"));
        Assert.Contains("apikey", ApproovService.GetSubstitutionQueryParams());
        Assert.True(ApproovService.GetExclusionURLRegexs().ContainsKey("health"));
    }

    [Fact]
    public void Initialize_SameConfigWithReinitComment_CallsSdkAgain()
    {
        ApproovService.Initialize("dummy-config");
        int callsBefore = ApproovService.InitCallCount;
        ApproovService.Initialize("dummy-config", "reinit-test");
        Assert.Equal(callsBefore + 1, ApproovService.InitCallCount);
    }

    [Fact]
    public void Initialize_SameConfigWithOptionsComment_ForwardsToSdk()
    {
        ApproovService.Initialize("dummy-config");
        int callsBefore = ApproovService.InitCallCount;
        ApproovService.Initialize("dummy-config", "options:fast");
        Assert.Equal(callsBefore + 1, ApproovService.InitCallCount);
    }

    [Fact]
    public void Initialize_SameConfigWithOptionsComment_FailureSurfacesAndStateUnchanged()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        ApproovService.NextInitShouldThrow = true;

        Assert.Throws<Exception>(() =>
            ApproovService.Initialize("dummy-config", "options:xyz"));

        Assert.Equal(2, ApproovService.InitCallCount);
        Assert.True(ApproovService.IsApproovEnabled());
        var (header, prefix) = ApproovService.GetApproovTokenHeader();
        Assert.Equal("X-Custom", header);
        Assert.Equal("Bearer ", prefix);
    }

    [Fact]
    public void Initialize_SameConfigReinit_ForwardedAndPlatformFalseTreatedAsSuccess()
    {
        ApproovService.Initialize("dummy-config");
        var custom = new TestMutator();
        ApproovService.SetServiceMutator(custom);
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        ApproovService.SetApproovTraceIDHeader("X-Trace");
        ApproovService.SetBindingHeader("Authorization");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.AddSubstitutionHeader("X-Secret", "prefix-");
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.AddExclusionURLRegex("health", ".*health.*");
        ApproovService.NextInitReturnsFalse = true;

        ApproovService.Initialize("dummy-config");

        Assert.Equal(2, ApproovService.InitCallCount);
        Assert.True(ApproovService.IsApproovEnabled());
        Assert.Same(custom, ApproovService.GetServiceMutator());
        var (header, prefix) = ApproovService.GetApproovTokenHeader();
        Assert.Equal("X-Custom", header);
        Assert.Equal("Bearer ", prefix);
        Assert.Equal("X-Trace", ApproovService.GetApproovTraceIDHeader());
        Assert.Equal("Authorization", ApproovService.GetBindingHeader());
        Assert.True(ApproovService.GetUseApproovStatusIfNoToken());
        Assert.True(ApproovService.GetSubstitutionHeaders().ContainsKey("X-Secret"));
        Assert.Contains("api_key", ApproovService.GetSubstitutionQueryParams());
        Assert.True(ApproovService.GetExclusionURLRegexs().ContainsKey("health"));
    }

    [Fact]
    public void Initialize_DifferentConfigRejected_StateCompletelyUnchanged()
    {
        ApproovService.Initialize("config-a");
        var custom = new TestMutator();
        ApproovService.SetServiceMutator(custom);
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        ApproovService.AddSubstitutionHeader("Authorization", null);
        ApproovService.AddExclusionURLRegex("health", ".*health.*");
        ApproovService.NextInitShouldThrow = true;

        Assert.Throws<Exception>(() => ApproovService.Initialize("config-b"));

        Assert.Equal(2, ApproovService.InitCallCount);
        Assert.True(ApproovService.IsApproovEnabled());
        Assert.Same(custom, ApproovService.GetServiceMutator());
        var (header, prefix) = ApproovService.GetApproovTokenHeader();
        Assert.Equal("X-Custom", header);
        Assert.Equal("Bearer ", prefix);
        Assert.True(ApproovService.GetSubstitutionHeaders().ContainsKey("Authorization"));
        Assert.True(ApproovService.GetExclusionURLRegexs().ContainsKey("health"));
    }

    [Fact]
    public void Initialize_FirstInitFails_RemainsUninitialized()
    {
        ApproovService.AddSubstitutionHeader("Authorization", null);
        ApproovService.AddExclusionURLRegex("health", ".*health.*");
        ApproovService.NextInitShouldThrow = true;

        Assert.Throws<Exception>(() => ApproovService.Initialize("real-config"));

        Assert.False(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());
        Assert.True(ApproovService.GetSubstitutionHeaders().ContainsKey("Authorization"));
        Assert.True(ApproovService.GetExclusionURLRegexs().ContainsKey("health"));
    }

    [Fact]
    public void Initialize_DefaultComment_ForwardsNullToPlatform()
    {
        ApproovService.Initialize("dummy-config");
        Assert.True(ApproovService.LastInitCommentWasNull);
        Assert.Null(ApproovService.LastInitComment);
    }

    [Fact]
    public void Initialize_ExplicitComment_ForwardsVerbatim()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.Initialize("dummy-config", "reinit-x");
        Assert.False(ApproovService.LastInitCommentWasNull);
        Assert.Equal("reinit-x", ApproovService.LastInitComment);
    }

    [Fact]
    public void Initialize_SameConfigReinit_PreservesCustomMutator()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new TestMutator());

        ApproovService.Initialize("dummy-config");

        Assert.IsType<TestMutator>(ApproovService.GetServiceMutator());
    }

    [Fact]
    public void Initialize_BypassUpgrade_PreservesCustomMutator()
    {
        ApproovService.Initialize("");
        var custom = new TestMutator();
        ApproovService.SetServiceMutator(custom);
        ApproovService.SetApproovTokenHeader("X-Custom", "Bearer ");
        ApproovService.SetApproovTraceIDHeader("X-Trace");
        ApproovService.SetBindingHeader("Authorization");
        ApproovService.SetUseApproovStatusIfNoToken(true);
        ApproovService.AddSubstitutionHeader("X-Secret", null);
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.AddExclusionURLRegex("health", ".*health.*");

        ApproovService.Initialize("real-config");

        Assert.True(ApproovService.IsApproovEnabled());
        Assert.Same(custom, ApproovService.GetServiceMutator());
        Assert.Equal(("Approov-Token", ""), ApproovService.GetApproovTokenHeader());
        Assert.Equal("Approov-TraceID", ApproovService.GetApproovTraceIDHeader());
        Assert.Null(ApproovService.GetBindingHeader());
        Assert.False(ApproovService.GetUseApproovStatusIfNoToken());
        Assert.Empty(ApproovService.GetSubstitutionHeaders());
        Assert.Empty(ApproovService.GetSubstitutionQueryParams());
        Assert.Empty(ApproovService.GetExclusionURLRegexs());
    }

    [Fact]
    public void Initialize_BypassUpgradeFails_PreservesInitializedBypassState()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());

        ApproovService.NextInitShouldThrow = true;
        Assert.Throws<Exception>(() => ApproovService.Initialize("real-config"));

        // Must remain initialized-but-disabled
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());
    }

    private sealed class TestMutator : IApproovServiceMutator
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
