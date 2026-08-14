// ApproovService.MAUI.Tests/ApproovServiceSdkTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceSdkTests : IDisposable
{
    public ApproovServiceSdkTests()
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
    public void Precheck_NotInitialized_Throws()
    {
        Assert.Throws<InitializationFailureException>(() => ApproovService.Precheck());
    }

    [Fact]
    public void Precheck_BypassMode_ThrowsPermanentExceptionWithoutSdkCall()
    {
        ApproovService.Initialize("");
        Assert.Throws<PermanentException>(() => ApproovService.Precheck());
        Assert.Equal(0, ApproovService.FetchCallCount);
        Assert.Equal(0, ApproovService.SecureStringCallCount);
    }

    [Fact]
    public void Precheck_Initialized_FetchesDummySecureStringKey()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.UnknownKey };
        ApproovService.Precheck();
        Assert.Equal(0, ApproovService.FetchCallCount);
        Assert.Equal(1, ApproovService.SecureStringCallCount);
        Assert.Equal("precheck-dummy-key", ApproovService.LastSecureStringKey);
        Assert.Null(ApproovService.LastSecureStringNewDef);
    }

    [Fact]
    public void Precheck_Rejected_ThrowsRejectionException()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextSecureStringResult = new StubTokenFetchResult
        {
            Status = ApproovTokenFetchStatus.Rejected,
            ARC = "ARC-123",
            RejectionReasons = "rooted"
        };

        var ex = Assert.Throws<RejectionException>(() => ApproovService.Precheck());
        Assert.Equal("ARC-123", ex.ARC);
        Assert.Equal("rooted", ex.RejectionReasons);
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
        Assert.Equal(0, ApproovService.FetchCallCount);
    }

    [Fact]
    public void FetchApproovToken_Initialized_ForwardsUrlAndReturnsToken()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Success, Token = "token-123" };

        var result = ApproovService.FetchApproovToken("https://api.example.com/data");

        Assert.Equal("token-123", result.Token);
        Assert.Equal("https://api.example.com/data", ApproovService.LastFetchUrl);
        Assert.Equal(1, ApproovService.FetchCallCount);
    }

    [Theory]
    [InlineData(ApproovTokenFetchStatus.NoNetwork)]
    [InlineData(ApproovTokenFetchStatus.PoorNetwork)]
    [InlineData(ApproovTokenFetchStatus.MitmDetected)]
    public void FetchApproovToken_NetworkFailures_ThrowRetryableError(
        ApproovTokenFetchStatus status)
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult { Status = status };

        var ex = Assert.Throws<NetworkingErrorException>(
            () => ApproovService.FetchApproovToken("https://example.com"));

        Assert.True(ex.ShouldRetry);
    }

    [Fact]
    public void FetchApproovToken_PermanentStatus_ThrowsPermanentException()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.NextFetchResult = new StubTokenFetchResult
            { Status = ApproovTokenFetchStatus.Disabled };

        Assert.Throws<PermanentException>(
            () => ApproovService.FetchApproovToken("https://example.com"));
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

    [Fact]
    public void FetchConfig_Initialized_ReturnsPlatformConfig()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.FetchConfigResult = "new-config";
        Assert.Equal("new-config", ApproovService.FetchConfig());
    }

    [Fact]
    public void SetDevKey_BypassMode_DoesNotCallPlatform()
    {
        ApproovService.Initialize("");
        ApproovService.SetDevKey("dev-key");
        Assert.Equal(0, ApproovService.DevKeyCallCount);
        Assert.Null(ApproovService.LastDevKey);
    }

    [Fact]
    public void SetDevKey_Initialized_ForwardsToPlatform()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.SetDevKey("dev-key");
        Assert.Equal(1, ApproovService.DevKeyCallCount);
        Assert.Equal("dev-key", ApproovService.LastDevKey);
    }

    [Fact]
    public void AccountMessageSignature_BypassMode_ReturnsNullWithoutPlatformCall()
    {
        ApproovService.Initialize("");
        Assert.Null(ApproovService.GetAccountMessageSignature("message"));
        Assert.Equal(0, ApproovService.AccountSignatureCallCount);
    }

    [Fact]
    public void AccountMessageSignature_Initialized_ForwardsMessage()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.AccountSignatureResult = "account-signature";
        Assert.Equal("account-signature",
            ApproovService.GetAccountMessageSignature("message"));
        Assert.Equal(1, ApproovService.AccountSignatureCallCount);
        Assert.Equal("message", ApproovService.LastAccountSignatureMessage);
    }

    [Fact]
    public void InstallMessageSignature_BypassMode_ReturnsNullWithoutPlatformCall()
    {
        ApproovService.Initialize("");
        Assert.Null(ApproovService.GetInstallMessageSignature("message"));
        Assert.Equal(0, ApproovService.InstallSignatureCallCount);
    }

    [Fact]
    public void InstallMessageSignature_Initialized_ForwardsMessage()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.InstallSignatureResult = "install-signature";
        Assert.Equal("install-signature",
            ApproovService.GetInstallMessageSignature("message"));
        Assert.Equal(1, ApproovService.InstallSignatureCallCount);
        Assert.Equal("message", ApproovService.LastInstallSignatureMessage);
    }
}
