using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceRequestProcessingEdgeTests : IDisposable
{
    public ApproovServiceRequestProcessingEdgeTests()
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
    public void UpdateRequest_CustomTokenHeaderAndPrefix_ReplacesExistingHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTokenHeader("X-Approov", "Bearer ");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, Token = "token-123" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("X-Approov", "old-token");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.False(response.Request!.Headers.Contains("Approov-Token"));
        Assert.Equal("Bearer token-123",
            string.Join("", response.Request.Headers.GetValues("X-Approov")));
    }

    [Fact]
    public void UpdateRequest_TraceIDHeader_ReplacesExistingHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTraceIDHeader("X-Approov-Trace");
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            TraceID = "trace-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        req.Headers.Add("X-Approov-Trace", "old-trace");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal("trace-123",
            string.Join("", response.Request!.Headers.GetValues("X-Approov-Trace")));
    }

    [Fact]
    public void UpdateRequest_TraceIDNull_DoesNotAddTraceHeader()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTraceIDHeader("X-Approov-Trace");
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.False(response.Request!.Headers.Contains("X-Approov-Trace"));
    }

    [Theory]
    [InlineData(ApproovTokenFetchStatus.NoApproovService)]
    [InlineData(ApproovTokenFetchStatus.UnknownUrl)]
    [InlineData(ApproovTokenFetchStatus.UnprotectedUrl)]
    public void UpdateRequest_NoTokenProceedStatuses_SendRequestWithoutToken(
        ApproovTokenFetchStatus status)
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = status, Token = "" };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.False(response.Request!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public void UpdateRequest_RejectedResult_ReturnsFailAndStoresLastArc()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Rejected,
            ARC = "ARC-999",
            RejectionReasons = "debugger"
        };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldFail, response.Decision);
        Assert.IsType<RejectionException>(response.Error);
        Assert.Equal("ARC-999", ApproovService.GetLastARC());
    }

    [Fact]
    public void UpdateRequest_MutatorThrows_ReturnsFailDecision()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetServiceMutator(new ThrowingMutator());
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldFail, response.Decision);
        Assert.IsType<InvalidOperationException>(response.Error);
    }

    [Fact]
    public void UpdateRequest_ProcessedMutatorReceivesMutationMetadata()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTraceIDHeader("X-Approov-Trace");
        ApproovService.AddSubstitutionHeader("X-Api-Key", "prefix-");
        ApproovService.AddSubstitutionQueryParam("api_key");
        var mutator = new CapturingMutator();
        ApproovService.SetServiceMutator(mutator);
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            TraceID = "trace-123"
        };
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://example.com/api?api_key=query-placeholder&other=value");
        req.Headers.Add("X-Api-Key", "prefix-header-placeholder");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.NotNull(mutator.Mutations);
        Assert.Equal("Approov-Token", mutator.Mutations!.TokenHeaderKey);
        Assert.Equal("X-Approov-Trace", mutator.Mutations.TraceIDHeaderKey);
        Assert.Equal("https://example.com/api?api_key=query-placeholder&other=value",
            mutator.Mutations.OriginalURL);
        Assert.Contains("X-Api-Key", mutator.Mutations.SubstitutionHeaderKeys);
        Assert.Contains("api_key", mutator.Mutations.SubstitutionQueryParamKeys);
        Assert.Equal("prefix-stub-secret",
            string.Join("", response.Request!.Headers.GetValues("X-Api-Key")));
        Assert.Contains("api_key=stub-secret",
            Uri.UnescapeDataString(response.Request.RequestUri!.Query));
    }

    private sealed class ThrowingMutator : PassthroughMutator
    {
        public override bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request) =>
            throw new InvalidOperationException("mutator failed");
    }

    private sealed class CapturingMutator : PassthroughMutator
    {
        internal ApproovRequestMutations? Mutations { get; private set; }

        public override HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage request, ApproovRequestMutations changes)
        {
            Mutations = changes;
            return request;
        }
    }

    private class PassthroughMutator : IApproovServiceMutator
    {
        public void HandlePrecheckResult(IApproovTokenFetchResult result) { }
        public void HandleFetchTokenResult(IApproovTokenFetchResult result) { }
        public void HandleFetchSecureStringResult(
            IApproovTokenFetchResult result, string operation, string key) { }
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult result) { }
        public virtual bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request) => true;
        public bool HandleInterceptorFetchTokenResult(
            IApproovTokenFetchResult result, string url) =>
            result.Status == ApproovTokenFetchStatus.Success;
        public bool HandleInterceptorHeaderSubstitutionResult(
            IApproovTokenFetchResult result, string header) =>
            result.Status == ApproovTokenFetchStatus.Success;
        public bool HandleInterceptorQueryParamSubstitutionResult(
            IApproovTokenFetchResult result, string queryKey) =>
            result.Status == ApproovTokenFetchStatus.Success;
        public virtual HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage request, ApproovRequestMutations changes) => request;
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) => true;
    }
}
