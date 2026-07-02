// ApproovService.MAUI.Tests/ApproovServiceSdkTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceSdkTests : IDisposable
{
    public void Dispose() => ApproovService.ResetForTesting();

    [Fact]
    public void Precheck_NotInitialized_Throws()
    {
        Assert.Throws<InitializationFailureException>(() => ApproovService.Precheck());
    }

    [Fact]
    public void Precheck_BypassMode_DoesNotThrow()
    {
        ApproovService.Initialize("");
        ApproovService.Precheck();
    }

    [Fact]
    public void FetchApproovToken_NotInitialized_Throws()
    {
        Assert.Throws<InitializationFailureException>(
            () => ApproovService.FetchApproovToken("https://example.com"));
    }

    [Fact]
    public void FetchApproovToken_BypassMode_ReturnsEmptyToken()
    {
        ApproovService.Initialize("");
        var result = ApproovService.FetchApproovToken("https://example.com");
        Assert.Equal("", result.Token);
        Assert.Equal(ApproovTokenFetchStatus.UnknownUrl, result.Status);
    }

    [Fact]
    public void GetDeviceID_BypassMode_ReturnsNull()
    {
        ApproovService.Initialize("");
        Assert.Null(ApproovService.GetDeviceID());
    }

    [Fact]
    public void GetDeviceID_StubMode_ReturnsTestId()
    {
        ApproovService.Initialize("dummy-config");
        Assert.Equal("test-device-id", ApproovService.GetDeviceID());
    }

    [Fact]
    public void FetchConfig_BypassMode_ReturnsNull()
    {
        ApproovService.Initialize("");
        Assert.Null(ApproovService.FetchConfig());
    }
}
