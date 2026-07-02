namespace ApproovSDK
{
    public enum ApproovTokenFetchStatus : long
    {
        Success = 0,
        NoNetwork = 1,
        MitmDetected = 2,
        PoorNetwork = 3,
        Disabled = 4,
        UnknownKey = 5,
        Rejected = 6,
        UnknownUrl = 7,
        UnprotectedUrl = 8,
        NoApproovService = 9,
        BadPayload = 10,
        InternalError = 11
    }
}
