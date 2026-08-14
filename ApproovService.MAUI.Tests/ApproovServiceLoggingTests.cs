using System.Linq;
using System.Net.Http;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceLoggingTests : IDisposable
{
    public ApproovServiceLoggingTests()
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
    public void Log_ReachesPlatformSink()
    {
        // Previously the only sink was Debug.WriteLine, which is [Conditional("DEBUG")] and
        // was therefore erased from every release build: the shipped package logged nothing.
        ApproovService.Initialize("test-config");

        Assert.Contains(ApproovService.LogLines,
            line => line.Contains("ApproovService initialized"));
    }

    [Fact]
    public void SetLoggingLevel_Off_SuppressesOutput()
    {
        ApproovService.SetLoggingLevel(ApproovLogLevel.Off);
        ApproovService.Initialize("test-config");

        Assert.Empty(ApproovService.LogLines);
    }

    [Fact]
    public void SetLoggingLevel_Error_SuppressesInfoButKeepsErrors()
    {
        ApproovService.SetLoggingLevel(ApproovLogLevel.Error);
        ApproovService.Initialize("test-config");
        Assert.DoesNotContain(ApproovService.LogLines,
            line => line.Contains("[Info]"));

        // The stub returns no install signature, so signing takes its fail-open path.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        ApproovService.UpdateRequestWithApproov(request);

        Assert.Contains(ApproovService.LogLines, line => line.Contains("[Error]"));
    }

    [Fact]
    public void SigningFailOpen_IsReported()
    {
        // A request that proceeds without a message signature must be visible to the
        // operator. This is the path that was silent in every release build.
        ApproovService.Initialize("test-config");
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");

        var result = ApproovService.UpdateRequestWithApproov(request);

        Assert.Equal(ApproovFetchDecision.ShouldProceed, result.Decision);
        Assert.False(request.Headers.Contains("Signature"));
        Assert.Contains(ApproovService.LogLines,
            line => line.Contains("proceeding unsigned"));
    }
}
