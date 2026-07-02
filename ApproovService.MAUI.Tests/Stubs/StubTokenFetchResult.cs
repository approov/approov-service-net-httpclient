namespace Approov.Tests;

public class StubTokenFetchResult : IApproovTokenFetchResult
{
    public ApproovTokenFetchStatus Status { get; init; } = ApproovTokenFetchStatus.Success;
    public string Token { get; init; } = "";
    public string? SecureString { get; init; } = null;
    public string ARC { get; init; } = "";
    public string RejectionReasons { get; init; } = "";
    public bool IsConfigChanged { get; init; } = false;
    public bool IsForceApplyPins { get; init; } = false;
    public string LoggableToken { get; init; } = "";
    public string? TraceID { get; init; } = null;
}
