namespace ApproovSDK
{
    // Must match the native NS_ENUM(NSUInteger, ApproovTokenFetchStatus) in Approov.framework/Headers/Approov.h
    public enum ApproovTokenFetchStatus : long
    {
        Success = 0,
        NoNetwork = 1,
        MitmDetected = 2,
        PoorNetwork = 3,
        NoApproovService = 4,
        BadUrl = 5,
        UnknownUrl = 6,
        UnprotectedUrl = 7,
        NotInitialized = 8,
        Rejected = 9,
        Disabled = 10,
        UnknownKey = 11,
        BadKey = 12,
        BadPayload = 13,
        InternalError = 14
    }
}
