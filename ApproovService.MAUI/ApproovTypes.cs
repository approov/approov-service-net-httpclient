using System.Net.Http;

namespace Approov;

public enum ApproovLogLevel { Off = 0, Error = 1, Warning = 2, Info = 3, Debug = 4 }

public enum ApproovFetchDecision { ShouldProceed, ShouldRetry, ShouldFail, ShouldIgnore }

public enum ApproovTokenFetchStatus
{
    Success = 0, NoNetwork = 1, MitmDetected = 2, PoorNetwork = 3,
    Disabled = 4, UnknownKey = 5, Rejected = 6, UnknownUrl = 7,
    UnprotectedUrl = 8, NoApproovService = 9, BadPayload = 10, InternalError = 11
}

public interface IApproovTokenFetchResult
{
    ApproovTokenFetchStatus Status { get; }
    string Token { get; }
    string? SecureString { get; }
    string ARC { get; }
    string RejectionReasons { get; }
    bool IsConfigChanged { get; }
    bool IsForceApplyPins { get; }
    string LoggableToken { get; }
    string? TraceID { get; }
}

public struct ApproovUpdateResponse
{
    public HttpRequestMessage? Request;
    public ApproovFetchDecision Decision;
    public string? SdkMessage;
    public Exception? Error;
}

public class ApproovException : Exception
{
    public bool ShouldRetry { get; }
    public ApproovException(string message, bool shouldRetry = false)
        : base(message) => ShouldRetry = shouldRetry;
    public ApproovException(string message, Exception inner, bool shouldRetry = false)
        : base(message, inner) => ShouldRetry = shouldRetry;
}

public class InitializationFailureException : ApproovException
{
    public InitializationFailureException(string message) : base(message) { }
}

public class ConfigurationFailureException : ApproovException
{
    public ConfigurationFailureException(string message) : base(message) { }
}

public class PinningErrorException : ApproovException
{
    public PinningErrorException(string message) : base(message) { }
}

public class NetworkingErrorException : ApproovException
{
    public NetworkingErrorException(string message, bool shouldRetry = true)
        : base(message, shouldRetry) { }
}

public class PermanentException : ApproovException
{
    public PermanentException(string message) : base(message) { }
    public PermanentException(string message, Exception inner) : base(message, inner) { }
}

public class RejectionException : ApproovException
{
    public string ARC { get; }
    public string RejectionReasons { get; }
    public RejectionException(string message, string arc = "", string rejectionReasons = "")
        : base(message) { ARC = arc; RejectionReasons = rejectionReasons; }
}
