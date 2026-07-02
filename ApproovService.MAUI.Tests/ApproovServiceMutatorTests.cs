// ApproovService.MAUI.Tests/ApproovServiceMutatorTests.cs
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

public class ApproovServiceMutatorTests
{
    private readonly IApproovServiceMutator _sut = ApproovServiceMutatorDefault.Shared;

    private static StubTokenFetchResult ResultWith(ApproovTokenFetchStatus status) =>
        new() { Status = status };

    // --- HandlePrecheckResult ---

    [Fact]
    public void HandlePrecheckResult_Success_DoesNotThrow()
    {
        _sut.HandlePrecheckResult(ResultWith(ApproovTokenFetchStatus.Success));
    }

    [Fact]
    public void HandlePrecheckResult_Rejected_ThrowsRejectionException()
    {
        var r = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Rejected,
            ARC = "ARC1", RejectionReasons = "r1"
        };
        var ex = Assert.Throws<RejectionException>(() => _sut.HandlePrecheckResult(r));
        Assert.Equal("ARC1", ex.ARC);
    }

    [Fact]
    public void HandlePrecheckResult_NoNetwork_ThrowsNetworkingError()
    {
        Assert.Throws<NetworkingErrorException>(
            () => _sut.HandlePrecheckResult(ResultWith(ApproovTokenFetchStatus.NoNetwork)));
    }

    [Fact]
    public void HandlePrecheckResult_UnknownKey_DoesNotThrow()
    {
        _sut.HandlePrecheckResult(ResultWith(ApproovTokenFetchStatus.UnknownKey));
    }

    [Fact]
    public void HandlePrecheckResult_Disabled_ThrowsPermanentException()
    {
        Assert.Throws<PermanentException>(
            () => _sut.HandlePrecheckResult(ResultWith(ApproovTokenFetchStatus.Disabled)));
    }

    // --- HandleInterceptorFetchTokenResult ---

    [Fact]
    public void HandleInterceptorFetchTokenResult_Success_ReturnsTrue()
    {
        var result = _sut.HandleInterceptorFetchTokenResult(
            ResultWith(ApproovTokenFetchStatus.Success), "https://example.com");
        Assert.True(result);
    }

    [Fact]
    public void HandleInterceptorFetchTokenResult_UnprotectedUrl_ReturnsFalse()
    {
        var result = _sut.HandleInterceptorFetchTokenResult(
            ResultWith(ApproovTokenFetchStatus.UnprotectedUrl), "https://example.com");
        Assert.False(result);
    }

    [Fact]
    public void HandleInterceptorFetchTokenResult_NoNetwork_ThrowsNetworkingError()
    {
        Assert.Throws<NetworkingErrorException>(() =>
            _sut.HandleInterceptorFetchTokenResult(
                ResultWith(ApproovTokenFetchStatus.NoNetwork), "https://example.com"));
    }

    [Fact]
    public void HandleInterceptorFetchTokenResult_Rejected_ThrowsRejectionException()
    {
        var r = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Rejected,
            ARC = "ARC1", RejectionReasons = "r1"
        };
        var ex = Assert.Throws<RejectionException>(() =>
            _sut.HandleInterceptorFetchTokenResult(r, "https://example.com"));
        Assert.Equal("ARC1", ex.ARC);
    }

    // --- HandleInterceptorHeaderSubstitutionResult ---

    [Fact]
    public void HandleInterceptorHeaderSubstitutionResult_Success_ReturnsTrue()
    {
        Assert.True(_sut.HandleInterceptorHeaderSubstitutionResult(
            ResultWith(ApproovTokenFetchStatus.Success), "X-Header"));
    }

    [Fact]
    public void HandleInterceptorHeaderSubstitutionResult_UnknownKey_ReturnsFalse()
    {
        Assert.False(_sut.HandleInterceptorHeaderSubstitutionResult(
            ResultWith(ApproovTokenFetchStatus.UnknownKey), "X-Header"));
    }

    // --- HandleInterceptorProcessedRequest (default = pass-through) ---

    [Fact]
    public void HandleInterceptorProcessedRequest_Default_ReturnsSameRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var mutations = new ApproovRequestMutations();
        var result = _sut.HandleInterceptorProcessedRequest(req, mutations);
        Assert.Same(req, result);
    }

    // --- HandlePinningShouldProcessRequest ---

    [Fact]
    public void HandlePinningShouldProcessRequest_Default_ReturnsTrue()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        Assert.True(_sut.HandlePinningShouldProcessRequest(req));
    }
}
