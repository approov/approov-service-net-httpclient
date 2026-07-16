namespace Approov;

internal static class IOSTokenFetchStatusMapper
{
    internal static ApproovTokenFetchStatus Map(ApproovSDK.ApproovTokenFetchStatus status)
        => status switch
        {
            ApproovSDK.ApproovTokenFetchStatus.Success => ApproovTokenFetchStatus.Success,
            ApproovSDK.ApproovTokenFetchStatus.NoNetwork => ApproovTokenFetchStatus.NoNetwork,
            ApproovSDK.ApproovTokenFetchStatus.MitmDetected => ApproovTokenFetchStatus.MitmDetected,
            ApproovSDK.ApproovTokenFetchStatus.PoorNetwork => ApproovTokenFetchStatus.PoorNetwork,
            ApproovSDK.ApproovTokenFetchStatus.NoApproovService => ApproovTokenFetchStatus.NoApproovService,
            ApproovSDK.ApproovTokenFetchStatus.BadUrl => ApproovTokenFetchStatus.InternalError,
            ApproovSDK.ApproovTokenFetchStatus.UnknownUrl => ApproovTokenFetchStatus.UnknownUrl,
            ApproovSDK.ApproovTokenFetchStatus.UnprotectedUrl => ApproovTokenFetchStatus.UnprotectedUrl,
            ApproovSDK.ApproovTokenFetchStatus.NotInitialized => ApproovTokenFetchStatus.InternalError,
            ApproovSDK.ApproovTokenFetchStatus.Rejected => ApproovTokenFetchStatus.Rejected,
            ApproovSDK.ApproovTokenFetchStatus.Disabled => ApproovTokenFetchStatus.Disabled,
            ApproovSDK.ApproovTokenFetchStatus.UnknownKey => ApproovTokenFetchStatus.UnknownKey,
            ApproovSDK.ApproovTokenFetchStatus.BadKey => ApproovTokenFetchStatus.InternalError,
            ApproovSDK.ApproovTokenFetchStatus.BadPayload => ApproovTokenFetchStatus.BadPayload,
            ApproovSDK.ApproovTokenFetchStatus.InternalError => ApproovTokenFetchStatus.InternalError,
            _ => ApproovTokenFetchStatus.InternalError
        };
}
