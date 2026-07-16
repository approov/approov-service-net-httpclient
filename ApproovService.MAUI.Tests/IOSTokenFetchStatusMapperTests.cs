using Xunit;

namespace Approov.Tests;

public class IOSTokenFetchStatusMapperTests
{
    [Theory]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.Success, ApproovTokenFetchStatus.Success)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.NoNetwork, ApproovTokenFetchStatus.NoNetwork)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.MitmDetected, ApproovTokenFetchStatus.MitmDetected)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.PoorNetwork, ApproovTokenFetchStatus.PoorNetwork)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.NoApproovService, ApproovTokenFetchStatus.NoApproovService)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.BadUrl, ApproovTokenFetchStatus.InternalError)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.UnknownUrl, ApproovTokenFetchStatus.UnknownUrl)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.UnprotectedUrl, ApproovTokenFetchStatus.UnprotectedUrl)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.NotInitialized, ApproovTokenFetchStatus.InternalError)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.Rejected, ApproovTokenFetchStatus.Rejected)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.Disabled, ApproovTokenFetchStatus.Disabled)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.UnknownKey, ApproovTokenFetchStatus.UnknownKey)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.BadKey, ApproovTokenFetchStatus.InternalError)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.BadPayload, ApproovTokenFetchStatus.BadPayload)]
    [InlineData(ApproovSDK.ApproovTokenFetchStatus.InternalError, ApproovTokenFetchStatus.InternalError)]
    public void Map_MapsNativeStatusToServiceStatus(
        ApproovSDK.ApproovTokenFetchStatus nativeStatus,
        ApproovTokenFetchStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, IOSTokenFetchStatusMapper.Map(nativeStatus));
    }

    [Fact]
    public void Map_UnknownNativeStatus_ReturnsInternalError()
    {
        Assert.Equal(ApproovTokenFetchStatus.InternalError,
            IOSTokenFetchStatusMapper.Map((ApproovSDK.ApproovTokenFetchStatus)long.MaxValue));
    }
}
