using System.Net.Http;
using System.Security.Cryptography;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UpdateRequest_TraceIDNullOrEmpty_DoesNotAddTraceHeader(string? traceID)
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetApproovTraceIDHeader("X-Approov-Trace");
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            TraceID = traceID
        };
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.False(response.Request!.Headers.Contains("X-Approov-Trace"));
    }

    [Theory]
    [InlineData(ApproovTokenFetchStatus.NoApproovService)]
    [InlineData(ApproovTokenFetchStatus.UnknownUrl)]
    [InlineData(ApproovTokenFetchStatus.UnprotectedUrl)]
    public void UpdateRequest_UnprotectedStatus_ReturnsOriginalBeforeSubstitutionAndPostMutation(
        ApproovTokenFetchStatus status)
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AddSubstitutionHeader("X-Api-Key", null);
        ApproovService.AddSubstitutionQueryParam("api_key");
        var mutator = new CapturingDefaultMutator();
        ApproovService.SetServiceMutator(mutator);
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = status,
            Token = "must-not-use",
            TraceID = "must-not-use"
        };
        var originalUri = new Uri("https://example.com/api?api_key=query-placeholder");
        var req = new HttpRequestMessage(HttpMethod.Post, originalUri);
        req.Headers.Add("Approov-Token", "original-token");
        req.Headers.Add("Approov-TraceID", "original-trace");
        req.Headers.Add("X-Api-Key", "header-placeholder");

        var response = ApproovService.UpdateRequestWithApproov(req);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Same(req, response.Request);
        Assert.Equal(originalUri, response.Request!.RequestUri);
        Assert.Equal("original-token",
            string.Join("", response.Request.Headers.GetValues("Approov-Token")));
        Assert.Equal("original-trace",
            string.Join("", response.Request.Headers.GetValues("Approov-TraceID")));
        Assert.Equal("header-placeholder",
            string.Join("", response.Request.Headers.GetValues("X-Api-Key")));
        Assert.Equal(0, ApproovService.SecureStringCallCount);
        Assert.Equal(0, mutator.ProcessedCallCount);
    }

    [Fact]
    public void UpdateRequest_ConfigChanged_FetchesDynamicConfigBeforeProceeding()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.FetchConfigResult = "updated-config";
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            IsConfigChanged = true
        };

        var response = ApproovService.UpdateRequestWithApproov(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com"));

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal(1, ApproovService.FetchConfigCallCount);
        Assert.Equal(0, ApproovService.PinsCallCount);
    }

    [Fact]
    public void UpdateRequest_ForceApplyPins_RefreshesPinsAndReturnsRetry()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.PinsJson = "{\"example.com\":[\"new-pin\"]}";
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            IsForceApplyPins = true
        };

        var response = ApproovService.UpdateRequestWithApproov(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com"));

        Assert.Equal(ApproovFetchDecision.ShouldRetry, response.Decision);
        Assert.IsType<NetworkingErrorException>(response.Error);
        Assert.Equal(1, ApproovService.PinsCallCount);
        Assert.Equal("public-key-sha256", ApproovService.LastPinType);
        Assert.False(response.Request!.Headers.Contains("Approov-Token"));
    }

    [Fact]
    public void UpdateRequest_DefaultSignerSignsTokenAndTraceHeaders()
    {
        ApproovService.Initialize("dummy-config");
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ApproovService.InstallSignatureResult = Convert.ToBase64String(
            ec.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence));
        ApproovService.NextFetchResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Success,
            Token = "token-123",
            TraceID = "trace-123"
        };

        var response = ApproovService.UpdateRequestWithApproov(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/api"));

        Assert.Equal(ApproovFetchDecision.ShouldProceed, response.Decision);
        Assert.Equal("trace-123",
            string.Join("", response.Request!.Headers.GetValues("Approov-TraceID")));
        Assert.True(response.Request.Headers.Contains("Signature"));
        string signatureInput = string.Join("",
            response.Request.Headers.GetValues("Signature-Input"));
        Assert.Contains("\"approov-token\"", signatureInput);
        Assert.Contains("\"approov-traceid\"", signatureInput);
        Assert.Equal(1, ApproovService.InstallSignatureCallCount);
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

    private sealed class CapturingDefaultMutator : IApproovServiceMutator
    {
        internal int ProcessedCallCount { get; private set; }

        private static IApproovServiceMutator Base => ApproovServiceMutatorDefault.Shared;
        public void HandlePrecheckResult(IApproovTokenFetchResult result) =>
            Base.HandlePrecheckResult(result);
        public void HandleFetchTokenResult(IApproovTokenFetchResult result) =>
            Base.HandleFetchTokenResult(result);
        public void HandleFetchSecureStringResult(
            IApproovTokenFetchResult result, string operation, string key) =>
            Base.HandleFetchSecureStringResult(result, operation, key);
        public void HandleFetchCustomJWTResult(IApproovTokenFetchResult result) =>
            Base.HandleFetchCustomJWTResult(result);
        public bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request) =>
            Base.HandleInterceptorShouldProcessRequest(request);
        public bool HandleInterceptorFetchTokenResult(
            IApproovTokenFetchResult result, string url) =>
            Base.HandleInterceptorFetchTokenResult(result, url);
        public bool HandleInterceptorHeaderSubstitutionResult(
            IApproovTokenFetchResult result, string header) =>
            Base.HandleInterceptorHeaderSubstitutionResult(result, header);
        public bool HandleInterceptorQueryParamSubstitutionResult(
            IApproovTokenFetchResult result, string queryKey) =>
            Base.HandleInterceptorQueryParamSubstitutionResult(result, queryKey);
        public HttpRequestMessage HandleInterceptorProcessedRequest(
            HttpRequestMessage request, ApproovRequestMutations changes)
        {
            ProcessedCallCount++;
            return request;
        }
        public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) =>
            Base.HandlePinningShouldProcessRequest(request);
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
